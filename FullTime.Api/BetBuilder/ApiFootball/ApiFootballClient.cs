using System.Net;
using FullTime.Api.BetBuilder.Dtos;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Wraps the direct api-sports.io dashboard API (x-apisports-key header, not RapidAPI — see
// ApiFootballOptions). Carries over HighlightlyClient's quota-cooldown/throttle wrapper since
// production (unlike the manual-trigger-only FullTime.Api.Sandbox this was validated in) needs the
// same protection against one throttle response turning into repeated hammering.
public class ApiFootballClient(HttpClient httpClient, ILogger<ApiFootballClient> logger)
{
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan QuotaCooldown = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private static DateTime _quotaExhaustedUntilUtc = DateTime.MinValue;

    // /fixtures?live=all — confirmed via a real call 2026-09-06: returns every live match worldwide
    // in a single request, unlike Highlightly's one-call-per-league model. Filter down to tracked
    // leagues client-side after the call, not via a query param (there isn't one for "live in these
    // leagues only").
    public Task<List<FixtureDto>> GetLiveFixturesAsync(CancellationToken ct = default) =>
        GetListAsync<FixtureDto>("fixtures?live=all", ct);

    public Task<List<FixtureDto>> GetFixturesAsync(
        int leagueId, int season, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetListAsync<FixtureDto>(
            $"fixtures?league={leagueId}&season={season}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

    public Task<List<FixtureEventDto>> GetFixtureEventsAsync(long fixtureId, CancellationToken ct = default) =>
        GetListAsync<FixtureEventDto>($"fixtures/events?fixture={fixtureId}", ct);

    public Task<List<FixturePlayersResponseTeam>> GetFixturePlayerStatsAsync(long fixtureId, CancellationToken ct = default) =>
        GetListAsync<FixturePlayersResponseTeam>($"fixtures/players?fixture={fixtureId}", ct);

    public Task<List<FixtureStatisticsTeam>> GetFixtureStatisticsAsync(long fixtureId, CancellationToken ct = default) =>
        GetListAsync<FixtureStatisticsTeam>($"fixtures/statistics?fixture={fixtureId}", ct);

    // /odds?fixture={id}&bookmaker={id} — confirmed live 2026-09-10: filtering by bookmaker
    // server-side (rather than requesting all 6+ and discarding client-side) keeps the response
    // small and the parsing surface to just the one bookmaker we price from.
    public Task<List<OddsFixtureResponseDto>> GetOddsAsync(long fixtureId, long bookmakerId, CancellationToken ct = default) =>
        GetListAsync<OddsFixtureResponseDto>($"odds?fixture={fixtureId}&bookmaker={bookmakerId}", ct);

    // /players/squads?team={id} — one call per team, used to resolve which team a the-odds-api
    // player-prop outcome belongs to (the-odds-api has no roster data of its own).
    public async Task<List<string>> GetSquadPlayerNamesAsync(long teamId, CancellationToken ct = default)
    {
        var teams = await GetListAsync<SquadDto>($"players/squads?team={teamId}", ct);
        // API-Football HTML-encodes some squad names (confirmed: "J. O'Brien" comes back as
        // "J. O&apos;Brien") — the-odds-api doesn't, so this needs decoding before any surname
        // comparison against it.
        return teams.FirstOrDefault()?.Players
            .Select(p => WebUtility.HtmlDecode(p.Name)).ToList() ?? [];
    }

    private async Task<List<T>> GetListAsync<T>(string requestUri, CancellationToken ct)
    {
        var result = await GetWithRetryAsync<ApiFootballResponse<T>>(requestUri, ct);
        return result?.Response ?? [];
    }

    private async Task<T?> GetWithRetryAsync<T>(string requestUri, CancellationToken ct)
    {
        var cooldownRemaining = _quotaExhaustedUntilUtc - DateTime.UtcNow;
        if (cooldownRemaining > TimeSpan.Zero)
        {
            throw new HttpRequestException(
                $"API-Football quota cooling down for another {cooldownRemaining.TotalSeconds:F0}s, skipping {requestUri}");
        }

        await WaitForThrottleSlotAsync(ct);
        using var response = await httpClient.GetAsync(requestUri, ct);
        var isThrottled = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden;
        if (!isThrottled)
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }

        var cooldown = response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > QuotaCooldown
            ? retryAfter
            : QuotaCooldown;
        _quotaExhaustedUntilUtc = DateTime.UtcNow.Add(cooldown);
        logger.LogWarning(
            "Throttled ({StatusCode}) on {RequestUri} - treating as quota exhaustion, cooling down all API-Football calls until {Until:O}",
            response.StatusCode, requestUri, _quotaExhaustedUntilUtc);
        response.EnsureSuccessStatusCode();
        return default;
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
