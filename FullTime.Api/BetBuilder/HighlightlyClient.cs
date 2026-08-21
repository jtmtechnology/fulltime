using System.Net;
using FullTime.Api.BetBuilder.Dtos;

namespace FullTime.Api.BetBuilder;

public class HighlightlyClient(HttpClient httpClient, ILogger<HighlightlyClient> logger)
{
    private const int MaxRetries = 4;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(300);
    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public async Task<List<MatchDto>> GetMatchesAsync(int leagueId, int season, DateOnly date, CancellationToken ct = default)
    {
        var result = await GetWithRetryAsync<MatchesResponse>(
            $"football/matches?leagueId={leagueId}&season={season}&date={date:yyyy-MM-dd}&limit=100", ct);
        return result?.Data ?? [];
    }

    // Every match worldwide on a given date, no leagueId filter — one paginated stream instead of
    // one call per tracked league. Callers paginate on the response's own Pagination.TotalCount.
    public async Task<MatchesResponse?> GetAllMatchesAsync(DateOnly date, int offset, CancellationToken ct = default)
    {
        return await GetWithRetryAsync<MatchesResponse>(
            $"football/matches?date={date:yyyy-MM-dd}&limit=100&offset={offset}", ct);
    }

    // Paginated at 5 matches per page by the provider (each match's own "odds" array carries every
    // bookmaker/market combination for it, so this stays 5 matches/page regardless of whether
    // bookmakerName filters it down) — callers loop, bumping offset by the page size until it
    // reaches the response's own totalCount. bookmakerName is optional: omitting it returns every
    // bookmaker's price for every market, which BetBuilderSyncService relies on for the 1X2 price
    // (deliberately not pinned to one bookmaker, unlike the Bet Builder extra markets).
    public async Task<OddsResponse?> GetOddsAsync(
        int leagueId, DateOnly date, int offset, string? bookmakerName = null, CancellationToken ct = default)
    {
        var bookmakerParam = bookmakerName is null ? "" : $"&bookmakerName={Uri.EscapeDataString(bookmakerName)}";
        return await GetWithRetryAsync<OddsResponse>(
            $"football/odds?leagueId={leagueId}&date={date:yyyy-MM-dd}{bookmakerParam}" +
            $"&oddsType=prematch&limit=5&offset={offset}", ct);
    }

    public async Task<List<MatchEventDto>> GetEventsAsync(long matchId, CancellationToken ct = default)
    {
        var result = await GetWithRetryAsync<List<MatchEventDto>>($"football/events/{matchId}", ct);
        return result ?? [];
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
