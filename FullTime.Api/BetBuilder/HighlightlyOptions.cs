namespace FullTime.Api.BetBuilder;

public class HighlightlyOptions
{
    public const string SectionName = "Highlightly";

    public required string ApiKey { get; set; }
    public string ApiHost { get; set; } = "sport-highlights-api.p.rapidapi.com";

    // How far ahead a match's kickoff can be and still be worth trying to price — this provider
    // only has pre-match odds for the next gameweek or so per league, same limitation every odds
    // provider we've looked at has.
    public int MatchWindowDays { get; set; } = 5;

    public int SyncIntervalMinutes { get; set; } = 60;

    // A single well-known bookmaker's prices, rather than showing every one of the 50+ bookmakers
    // this provider has per match — keeps sync payloads small (odds pages are capped at 5 matches
    // each) and gives a consistent, recognisable price source across the app.
    public string BookmakerName { get; set; } = "bet365";

    // How many dates (today..today+N-1) the primary match/score sync fetches every idle tick, via
    // the global (no leagueId) matches-by-date endpoint. Kept modest — fixtures this far out barely
    // change — since this multiplies directly into daily call volume; see
    // HighlightlyMatchSyncService for the full quota calculation.
    public int MatchSyncDaysAhead { get; set; } = 6;

    // The primary match/score sync backs off to this cadence when nothing's live, and switches to
    // LiveRefreshIntervalSeconds whenever at least one tracked match is in progress.
    public int IdleRefreshIntervalSeconds { get; set; } = 300;
    public int LiveRefreshIntervalSeconds { get; set; } = 45;
}
