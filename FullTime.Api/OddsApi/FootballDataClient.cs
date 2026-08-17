using System.Net;
using FullTime.Api.OddsApi.Dtos;

namespace FullTime.Api.OddsApi;

public class FootballDataClient(HttpClient httpClient, ILogger<FootballDataClient> logger)
{
    private const int MaxRetries = 4;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(2500);
    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public async Task<List<MatchDto>> GetMatchesByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var dateParam = date.ToString("yyyyMMdd");
        var result = await GetWithRetryAsync<MatchesByDateResponse>(
            $"football-get-matches-by-date?date={dateParam}", ct);
        return result?.Response?.Matches ?? [];
    }

    public async Task<EventOddsResponse?> GetEventOddsAsync(long eventId, string countryCode, CancellationToken ct = default)
    {
        return await GetWithRetryAsync<EventOddsResponse>(
            $"football-event-odds?eventid={eventId}&countrycode={countryCode}", ct);
    }

    private async Task<T?> GetWithRetryAsync<T>(string requestUri, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await WaitForThrottleSlotAsync(ct);
            using var response = await httpClient.GetAsync(requestUri, ct);
            var isThrottled = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden;
            if (!isThrottled)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            }

            if (attempt >= MaxRetries)
            {
                response.EnsureSuccessStatusCode();
            }

            // This provider's free tier escalates a plain 429 to a temporary 403 under continued pressure,
            // so both are treated as transient throttling here rather than a real auth failure.
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 3);
            logger.LogInformation("Throttled ({StatusCode}) on {RequestUri}, retrying in {Delay}", response.StatusCode, requestUri, delay);
            await Task.Delay(delay, ct);
        }
    }

    private async Task WaitForThrottleSlotAsync(CancellationToken ct)
    {
        await _throttleGate.WaitAsync(ct);
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
            _throttleGate.Release();
        }
    }
}
