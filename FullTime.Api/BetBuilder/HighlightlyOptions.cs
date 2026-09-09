namespace FullTime.Api.BetBuilder;

public class HighlightlyOptions
{
    public const string SectionName = "Highlightly";

    public required string ApiKey { get; set; }

    // Direct Highlightly account (soccer.highlightly.net), not RapidAPI's proxy - confirmed real
    // 2026-09-06: same x-rapidapi-key/x-rapidapi-host header shape works on both, but they're
    // separate accounts/tokens/quotas entirely (RapidAPI's own "Sport Highlights API" quota was
    // fully exhausted; this direct "Football API" account is a fresh, separate 25,000/day bucket).
    public string ApiHost { get; set; } = "soccer.highlightly.net";

    // How far ahead a match's kickoff can be and still be worth trying to price — this provider
    // only has pre-match odds for the next gameweek or so per league, same limitation every odds
    // provider we've looked at has (and even within that window, coverage varies a lot by league —
    // confirmed some leagues have nothing priced 3 days out while others have prices 6+ days out).
    public int MatchWindowDays { get; set; } = 7;

    // Prices barely move once set, so this doesn't need anywhere near live-score frequency — every
    // 4 hours still catches matches that had no price at all yet (confirmed happening — some
    // fixtures simply aren't priced by any bookmaker until closer to kickoff) far sooner than
    // waiting up to 24h, while costing ~150-250 calls/run × 6 ≈ 900-1,500/day instead of ~6,000/day
    // at hourly — see IdleRefreshIntervalSeconds/LiveRefreshIntervalSeconds for how this trades off
    // against live-score polling budget.
    public int SyncIntervalMinutes { get; set; } = 240;

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

    // RefreshLiveAsync backs off to this cadence when nothing's live — no need for anything faster
    // than hourly, since nothing changes between matches.
    public int IdleRefreshIntervalSeconds { get; set; } = 3600;

    // ...and switches to this cadence whenever at least one tracked match is in progress. Costs a
    // fixed ~14 calls/tick (one call per tracked league, not a paginated worldwide fetch — see
    // HighlightlyMatchSyncService). At 15s that was ~3,360 calls/hour of live coverage, which on its
    // own exceeds the confirmed 25,000/day RapidAPI cap within an 8h heavy Saturday (staggered EFL
    // kickoffs through a Saturday-evening European night game) - a real risk, not just a tight
    // budget, once actually run through the numbers. 30s halves that to ~1,680 calls/hour, so even a
    // 10h heavy Saturday (~16,800) plus the ~1,100-1,700/day baseline (fixture discovery + odds sync
    // + goal-scorer resolution) lands around ~18,500 - comfortably under the cap with headroom to
    // spare. Score/status freshness at 30s vs 15s is not noticeable for a casual family app.
    public int LiveRefreshIntervalSeconds { get; set; } = 30;

    // How often BetBuilderSyncService.ResolveMatchEventsAsync runs, independent of
    // SyncIntervalMinutes — this used to share the odds-sync cadence, which meant a
    // FirstTeamToScore bet could sit unsettled for up to SyncIntervalMinutes after its match
    // actually finished (confirmed happening: ~4h delay once that moved to 240). Kept short since
    // it only touches matches Finished in the last 3 days and is cheap when nothing's pending.
    public int GoalScorerResolutionIntervalMinutes { get; set; } = 5;

    // Where HighlightlyClient sends quota alerts (see its RecordCallForQuotaTracking/
    // MaybeAlertExhausted) - null/empty disables alerting entirely rather than throwing, since a
    // personal project shouldn't hard-fail the whole sync loop over a missing alert address. Kept
    // out of the checked-in appsettings.json default (same as ApiKey/Email:* secrets) and set via
    // the Highlightly__AlertEmail environment variable on the VM instead.
    public string? AlertEmail { get; set; }

    // The account's actual daily cap (confirmed 25,000/day when the direct soccer.highlightly.net
    // account was set up - see HighlightlyMatchSyncService's top-of-file comment) - not derived from
    // anything the provider reports at runtime, since its 429 response carries no quota/reset header
    // at all (confirmed 2026-09-09: just a plain error message).
    public int DailyCallBudget { get; set; } = 25000;

    // RecordCallForQuotaTracking fires the proactive warning email once the day's call count crosses
    // this percentage of DailyCallBudget - 80% leaves headroom to actually receive and act on the
    // warning before the account hits its real cap.
    public int AlertThresholdPercent { get; set; } = 80;
}
