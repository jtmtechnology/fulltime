namespace FullTime.Api.BetBuilder.ApiFootball;

// Powers the "last 5 results" dots on the Bet Builder header. Backed by API-Football's own
// /teams/statistics "form" field (see ApiFootballClient.GetTeamFormAsync) rather than computed from
// our own Matches table - confirmed real 2026-09-10 that this gives a team's actual season-to-date
// form immediately, instead of waiting weeks for enough matches to complete under the new provider's
// own team-ID scheme. League-scoped: callers pass the match's own LeagueId, so this reflects "form
// in this specific competition", same convention most football apps use.
public class ApiFootballTeamFormService(ApiFootballClient client, ILogger<ApiFootballTeamFormService> logger)
{
    // Form only meaningfully changes once a match finishes, so a coarse cache is fine - this just
    // keeps repeated Bet Builder views (by the same or different users) from each costing a fresh
    // API-Football call. In-memory/static like HighlightlyClient's/ApiFootballClient's own
    // quota-cooldown state - resets on deploy, which is an acceptable cost for a display-only cache.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly Dictionary<(long TeamId, long LeagueId), (List<string> Form, DateTime FetchedAt)> Cache = new();

    public async Task<List<string>> GetFormAsync(long teamId, long leagueId, CancellationToken ct = default)
    {
        var key = (teamId, leagueId);

        await CacheLock.WaitAsync(ct);
        try
        {
            if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return cached.Form;
            }
        }
        finally
        {
            CacheLock.Release();
        }

        string? raw;
        try
        {
            raw = await client.GetTeamFormAsync((int)leagueId, SeasonFor(DateTime.UtcNow), teamId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Form is a nice-to-have display element - a failed/throttled lookup must never break
            // the rest of the Bet Builder page. Cache the miss too (same TTL), so a bad request
            // doesn't get retried on every single page view until the cache would naturally expire.
            logger.LogWarning(ex, "Failed to fetch team form for team {TeamId} league {LeagueId}", teamId, leagueId);
            raw = null;
        }

        var form = ParseForm(raw);

        await CacheLock.WaitAsync(ct);
        try
        {
            Cache[key] = (form, DateTime.UtcNow);
        }
        finally
        {
            CacheLock.Release();
        }

        return form;
    }

    // Keeps only W/D/L in case the provider ever includes another character for some edge case
    // (e.g. an abandoned/voided match) - a stray unrecognized letter would otherwise render as a
    // wrong-colored dot rather than being silently dropped. TakeLast(5) on an already-chronological
    // string preserves oldest-to-newest order within the slice.
    private static List<string> ParseForm(string? raw) =>
        string.IsNullOrEmpty(raw)
            ? []
            : raw.Where(c => c is 'W' or 'D' or 'L').TakeLast(5).Select(c => c.ToString()).ToList();

    private static int SeasonFor(DateTime date) => date.Month >= 7 ? date.Year : date.Year - 1;
}
