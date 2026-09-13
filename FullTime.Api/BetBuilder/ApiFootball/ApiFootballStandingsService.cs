using FullTime.Api.BetBuilder.Dtos;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Powers the league Table page. Backed by API-Football's /standings (see
// ApiFootballClient.GetStandingsAsync). A table only changes after matches finish, so a coarse
// cache avoids a fresh call every time someone opens it - same in-memory/static shape as
// ApiFootballTeamFormService, and for the same reason (resets on deploy, acceptable for a
// display-only cache; no background polling per the standing no-polling preference - this is purely
// on-demand, first view of the day pays for the fetch).
public class ApiFootballStandingsService(ApiFootballClient client, ILogger<ApiFootballStandingsService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly Dictionary<long, (List<StandingEntryDto> Standings, DateTime FetchedAt)> Cache = new();

    public async Task<List<StandingEntryDto>> GetStandingsAsync(long apiFootballLeagueId, CancellationToken ct = default)
    {
        await CacheLock.WaitAsync(ct);
        try
        {
            if (Cache.TryGetValue(apiFootballLeagueId, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return cached.Standings;
            }
        }
        finally
        {
            CacheLock.Release();
        }

        List<StandingEntryDto> standings;
        try
        {
            standings = await client.GetStandingsAsync((int)apiFootballLeagueId, SeasonFor(DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A knockout-only competition (no table) or a throttled/failed call both just mean "no
            // rows to show" - cache the miss too (same TTL) so a bad request isn't retried on every
            // single Table view until the cache would naturally expire.
            logger.LogWarning(ex, "Failed to fetch standings for league {LeagueId}", apiFootballLeagueId);
            standings = [];
        }

        await CacheLock.WaitAsync(ct);
        try
        {
            Cache[apiFootballLeagueId] = (standings, DateTime.UtcNow);
        }
        finally
        {
            CacheLock.Release();
        }

        return standings;
    }

    private static int SeasonFor(DateTime date) => date.Month >= 7 ? date.Year : date.Year - 1;
}
