namespace FullTime.Api.BetBuilder;

public class HighlightlyOptions
{
    public const string SectionName = "Highlightly";

    public required string ApiKey { get; set; }
    public string ApiHost { get; set; } = "sport-highlights-api.p.rapidapi.com";

    // How far ahead a match's kickoff can be and still be worth trying to price — this provider
    // only has pre-match odds for the next gameweek or so per league, same limitation every odds
    // provider we've looked at has (and even within that window, coverage varies a lot by league —
    // confirmed some leagues have nothing priced 3 days out while others have prices 6+ days out).
    public int MatchWindowDays { get; set; } = 7;

    // Once fixtures and prices are captured they barely change, so there's no need to keep
    // re-polling them anywhere near as often as live scores — this is a once-a-day cadence for both
    // this (Bet Builder markets + 1X2 odds) and HighlightlyMatchSyncService's fixture discovery.
    public int SyncIntervalMinutes { get; set; } = 1440;

    // A single well-known bookmaker's prices, rather than showing every one of the 50+ bookmakers
    // this provider has per match — keeps sync payloads small (odds pages are capped at 5 matches
    // each) and gives a consistent, recognisable price source across the app.
    public string BookmakerName { get; set; } = "bet365";

    // How many dates (today..today+N-1) fixture discovery fetches, one football/matches?leagueId=X
    // call per HighlightlyLeagueMap.TrackedLeagueIds entry per date. This runs once a day
    // (FixtureDiscoveryIntervalMinutes) rather than every tick — see
    // HighlightlyMatchSyncService.RefreshFixturesAsync — so it can afford to cover the same window
    // MatchWindowDays prices.
    public int MatchSyncDaysAhead { get; set; } = 7;

    // How often HighlightlyMatchSyncService.RefreshFixturesAsync (the full future-fixture window)
    // runs, separately from RefreshLiveAsync (today + any unfinished match, every tick). Fixtures
    // don't need re-discovering that often once captured.
    public int FixtureDiscoveryIntervalMinutes { get; set; } = 1440;

    // RefreshLiveAsync backs off to this cadence when nothing's live, and switches to
    // LiveRefreshIntervalSeconds whenever at least one tracked match is in progress. Scoped to just
    // today + catch-up dates (not the whole future window) and now costing a fixed ~14 calls/tick
    // (one call per tracked league, not a paginated worldwide fetch — see
    // HighlightlyMatchSyncService), so this affords a much shorter cadence than before without
    // meaningfully risking the daily quota. Sized against the 25,000 req/day tier: even a heavy
    // Saturday with ~10 live hours (120 ticks/hour × 14 calls at 30s) plus ~14 idle hours (20
    // ticks/hour × 14 calls at 180s) comes to roughly 21,000 calls/day including fixture discovery
    // and the Bet Builder/odds sync — leaves real headroom, but re-check real usage after a live
    // matchday if this ever gets pushed further.
    public int IdleRefreshIntervalSeconds { get; set; } = 180;
    public int LiveRefreshIntervalSeconds { get; set; } = 30;
}
