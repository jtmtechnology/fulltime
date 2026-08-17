namespace FullTime.Api.OddsApi;

public class OddsApiOptions
{
    public const string SectionName = "OddsApi";

    public required string ApiKey { get; set; }
    public string ApiHost { get; set; } = "free-api-live-football-data.p.rapidapi.com";

    // English men's senior pyramid + major cups: Premier League (47), Championship (48/938218),
    // League One (108/938219), League Two (109/938220), FA Cup (132), EFL Cup (133/938221),
    // Community Shield (247). The provider tags a new season's EFL competitions under a temporary
    // ID until it links them to the canonical one, so both are listed where that applies.
    public List<long> LeagueIds { get; set; } = [47, 48, 938218, 108, 938219, 109, 938220, 132, 133, 938221, 247];
    public string CountryCode { get; set; } = "GB";
    public int DaysAhead { get; set; } = 7;
    public int CacheMinutes { get; set; } = 10;

    // The background sync backs off to this cadence when nothing's live, and switches to
    // LiveRefreshIntervalSeconds whenever at least one tracked match is in progress.
    public int IdleRefreshIntervalSeconds { get; set; } = 600;
    public int LiveRefreshIntervalSeconds { get; set; } = 10;
}
