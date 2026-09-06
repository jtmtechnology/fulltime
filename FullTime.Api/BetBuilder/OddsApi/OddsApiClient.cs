using System.Net;
using FullTime.Api.BetBuilder.Dtos;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.OddsApi;

// Wraps api.the-odds-api.com/v4. The API key is a query-string param on this provider (?apiKey=),
// not a header, unlike Highlightly/API-Football.
public class OddsApiClient(HttpClient httpClient, IOptions<OddsApiOptions> options, ILogger<OddsApiClient> logger)
{
    // Confirmed real via production 2026-09-06: OddsApiMarketService.EnsureBetBuilderMarketsFreshAsync
    // fires up to 10 the-odds-api calls per match (events + 9 markets) in parallel via Task.WhenAll,
    // and a bulk prime across many matches at once (several matches processed concurrently) pushed
    // that well past whatever per-second rate the-odds-api actually enforces — real 429s came back,
    // which this class had no throttle to prevent (unlike HighlightlyClient/ApiFootballClient, which
    // both already serialize their own calls). AddHttpClient<OddsApiClient> makes this a transient
    // typed client (a new instance per resolution), so the gate has to be static to actually
    // serialize every outbound call process-wide, same reasoning as the other two clients.
    // 150ms (~6.7 req/sec) still produced real 429s under the concurrent load of priming many
    // matches at once (confirmed 2026-09-06: 64 residual throttle hits even with the one-retry
    // fallback) - the-odds-api's actual per-second cap is evidently tighter than assumed. Widened to
    // a more conservative gap; normal on-demand usage (one match's ~10 calls when a single user
    // opens Bet Builder) is nowhere near either interval, so this only slows down a bulk operation
    // like priming many matches at once, never a real user's single request.
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(400);
    private static readonly SemaphoreSlim ThrottleGate = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    private string ApiKey => options.Value.ApiKey;

    // Free — listing events doesn't cost quota, only GetEventOddsAsync does.
    public Task<List<OddsApiEventDto>> GetEventsAsync(string sportKey, CancellationToken ct = default) =>
        GetAsync<List<OddsApiEventDto>>($"v4/sports/{sportKey}/events?apiKey={ApiKey}", ct, fallback: []);

    // Costs 1 request per (market x region) requested.
    public Task<OddsApiEventOddsDto?> GetEventOddsAsync(
        string sportKey, string eventId, string markets, string regions = "uk", CancellationToken ct = default) =>
        GetAsync<OddsApiEventOddsDto?>(
            $"v4/sports/{sportKey}/events/{eventId}/odds?apiKey={ApiKey}&regions={regions}&markets={markets}&oddsFormat=decimal",
            ct, fallback: null);

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken ct, T fallback)
    {
        await WaitForThrottleSlotAsync(ct);
        using var response = await httpClient.GetAsync(requestUri, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // One retry after a real (if brief) pause, rather than the caller's whole on-demand
            // fetch silently coming back empty (indistinguishable from "genuinely not priced") -
            // this is a rate-limit response, not a "this match has no odds" signal.
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(500);
            logger.LogWarning("the-odds-api throttled (429) on {RequestUri}, retrying once after {Delay}ms", requestUri, retryAfter.TotalMilliseconds);
            await Task.Delay(retryAfter, ct);
            await WaitForThrottleSlotAsync(ct);
            using var retryResponse = await httpClient.GetAsync(requestUri, ct);
            retryResponse.EnsureSuccessStatusCode();
            return await retryResponse.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? fallback;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? fallback;
    }

    private static async Task WaitForThrottleSlotAsync(CancellationToken ct)
    {
        await ThrottleGate.WaitAsync(ct);
        try
        {
            var wait = MinRequestInterval - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            ThrottleGate.Release();
        }
    }
}
