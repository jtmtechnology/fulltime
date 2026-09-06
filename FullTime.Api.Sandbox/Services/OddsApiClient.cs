using System.Net.Http.Headers;
using FullTime.Api.Sandbox.Dtos;

namespace FullTime.Api.Sandbox.Services;

// Wraps api.the-odds-api.com/v4. The API key is a query-string param on this provider (?apiKey=),
// not a header, unlike Highlightly/API-Football - confirmed 2026-09-06.
public class OddsApiClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<Options.OddsApiOptions> options)
{
    private static readonly string[] RateLimitHeaderNames = ["x-requests-remaining", "x-requests-used", "x-requests-last"];

    private string ApiKey => options.Value.ApiKey;

    // Free - confirmed 2026-09-06 this doesn't move x-requests-used.
    public Task<ApiCallResult<List<OddsApiSportDto>>> GetSportsAsync(CancellationToken ct = default) =>
        GetAsync<List<OddsApiSportDto>>($"v4/sports/?apiKey={ApiKey}", ct);

    // Free - lists upcoming/live event IDs for a sport so a caller knows what to fetch odds for.
    public Task<ApiCallResult<List<OddsApiEventDto>>> GetEventsAsync(string sportKey, CancellationToken ct = default) =>
        GetAsync<List<OddsApiEventDto>>($"v4/sports/{sportKey}/events?apiKey={ApiKey}", ct);

    // Costs 1 request per (market x region) requested - confirmed empirically 2026-09-06 (6 markets
    // x 1 region = 6 requests; 1 market x 2 regions = 2 requests). Not free like the two above.
    public Task<ApiCallResult<OddsApiEventOddsDto>> GetEventOddsAsync(
        string sportKey, string eventId, string markets, string regions = "uk", CancellationToken ct = default) =>
        GetAsync<OddsApiEventOddsDto>(
            $"v4/sports/{sportKey}/events/{eventId}/odds?apiKey={ApiKey}&regions={regions}&markets={markets}&oddsFormat=decimal",
            ct);

    private async Task<ApiCallResult<T>> GetAsync<T>(string requestUri, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(requestUri, ct);
        var rateLimit = ExtractRateLimitInfo(response.Headers);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return new ApiCallResult<T>(body!, rateLimit);
    }

    private static RateLimitInfo ExtractRateLimitInfo(HttpResponseHeaders headers)
    {
        var found = new Dictionary<string, string>();
        foreach (var name in RateLimitHeaderNames)
        {
            if (headers.TryGetValues(name, out var values))
            {
                found[name] = string.Join(",", values);
            }
        }

        return new RateLimitInfo(found);
    }
}
