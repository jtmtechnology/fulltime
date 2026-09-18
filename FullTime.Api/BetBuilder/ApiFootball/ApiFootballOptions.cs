namespace FullTime.Api.BetBuilder.ApiFootball;

public class ApiFootballOptions
{
    public const string SectionName = "ApiFootball";

    // Direct api-sports.io dashboard key (x-apisports-key header), not a RapidAPI key — the two are
    // different accounts/keys even though API-Football is sold on both (confirmed evaluating this
    // in FullTime.Api.Sandbox: the account's existing RapidAPI key came back "not subscribed"
    // against the RapidAPI-hosted version of this same API).
    public required string ApiKey { get; set; }
    public string ApiHost { get; set; } = "v3.football.api-sports.io";

    // How far ahead fixture discovery looks per tracked league — mirrors HighlightlyOptions.
    // MatchSyncDaysAhead (confirmed live 2026-09-14: this was still 7 here, one day short of
    // Highlightly's 8, despite the comment already claiming parity - RefreshFixturesAsync's window
    // is today..today+(this-1), so 7 only reaches 6 days out, silently dropping the 7th day's
    // fixtures - e.g. real Premier League fixtures a week out were confirmed missing from the DB
    // even though the live provider already had them). PRO tier (7,500 req/day, 300 req/min)
    // comfortably supports this at the daily FixtureDiscoveryIntervalMinutes cadence.
    public int MatchSyncDaysAhead { get; set; } = 8;
    // Lowered 1440 (once/day) -> 60 (hourly) 2026-09-18 - the daily cadence left up to 24h before a
    // provider-side postponement on an Upcoming match got noticed at all (RefreshLiveAsync never
    // looks at Upcoming matches, only live/previously-InProgress ones - see
    // ApiFootballMatchSyncService's class comment and RecheckStaleUpcomingAsync below). 14 tracked
    // leagues x 24 ticks/day = 336 calls/day vs 14/day before - a ~322/day increase, trivial against
    // the 75,000/day Ultra budget.
    public int FixtureDiscoveryIntervalMinutes { get; set; } = 60;

    // RefreshLiveAsync backs off to this cadence when nothing's live.
    public int IdleRefreshIntervalSeconds { get; set; } = 3600;

    // ...and switches to this cadence whenever at least one tracked match is in progress. Unlike
    // Highlightly's ~14-calls-per-tick model (one per tracked league), this is ONE fixtures?live=all
    // call regardless of how many matches/leagues are live — the whole point of the cutover — so
    // this can safely run faster than Highlightly's 30s without remotely approaching PRO's 300
    // req/min cap.
    public int LiveRefreshIntervalSeconds { get; set; } = 15;

    // How often ApiFootballGoalScorerResolutionBackgroundService / player-stats resolution runs,
    // independent of fixture/live cadence — settlement-latency concern, not price-freshness (see
    // HighlightlyOptions.GoalScorerResolutionIntervalMinutes for the same reasoning).
    public int GoalScorerResolutionIntervalMinutes { get; set; } = 5;

    // How often RefreshLiveMatchEventsAsync re-fetches events for InProgress matches, for Match
    // Summary's live display. Unlike live-score sync (one fixtures?live=all call regardless of
    // match count), this is genuinely one call PER live match PER tick, so it's the dominant cost
    // in the quota math below. Lowered 45s -> 10s once the account moved to 75,000/day (worst-case
    // estimate: an 18-fixture heavy night at ~2h live each is ~18*(7200/10) = 12,960 calls just for
    // this, comfortably inside budget alongside live-score sync + everything else - see
    // DailyCallBudget/AlertThresholdPercent below for the safety net if that estimate is wrong).
    public int LiveEventsRefreshIntervalSeconds { get; set; } = 10;

    // The account's actual daily cap (75,000/day, upgraded from Pro's 7,500 - see HANDOVER.md).
    public int DailyCallBudget { get; set; } = 75000;

    // RecordCallForQuotaTracking fires the proactive warning email once the day's call count
    // crosses this percentage of DailyCallBudget - same 80% headroom Highlightly's alerting uses.
    public int AlertThresholdPercent { get; set; } = 80;

    // Bet365's api-sports.io bookmaker ID (confirmed live 2026-09-10: {"id":8,"name":"Bet365"}) -
    // same choice HighlightlyOptions.BookmakerName ("bet365") already made, for the richest
    // coverage of the bookmakers available on this plan. BookmakerName is the matching display
    // name/BookmakerLogos key for that same bookmaker (MatchesController.GetBetBuilderMarkets) -
    // kept as a separate field rather than derived from the ID, same pattern as Highlightly's own
    // BookmakerName.
    public long OddsBookmakerId { get; set; } = 8;
    public string BookmakerName { get; set; } = "bet365";

    // How often ApiFootballOddsSyncBackgroundService ticks and checks every tracked Upcoming match
    // against NeedsOddsRefresh's TTL gate - the gate (not this interval) is what actually controls
    // call volume, since most ticks are no-ops for matches not yet due. Mirrors OddsApiOptions'
    // tiered-TTL fields/reasoning exactly.
    public int OddsSyncIntervalMinutes { get; set; } = 15;
    public int OddsFarTtlHours { get; set; } = 6;
    public int OddsNearTtlMinutes { get; set; } = 45;
    public int OddsImminentTtlMinutes { get; set; } = 15;
    public int OddsNearBoundaryHours { get; set; } = 24;
    public int OddsImminentBoundaryHours { get; set; } = 1;

    // A match stuck at InProgress this long past its own kickoff (extra time + penalties + delays
    // all included, generously) almost certainly means the sync never saw its real final whistle -
    // abandoned game, or API-Football simply drops it from fixtures?live=all before sending a clean
    // "FT". Found 2026-09-10: HasLiveMatchAsync (which NextPollDelayAsync uses to decide fast-vs-idle
    // cadence) counted ANY InProgress match, so one stuck row would force the expensive 10s live
    // cadence forever - the exact same class of quota risk as the Postponed/staleKickoffs bug this
    // whole cutover already fixed once, just via a different trigger.
    public int StaleInProgressMinutes { get; set; } = 210;

    // A match still Upcoming this long past its own kickoff almost certainly means a postponement
    // (or other status change) our sync never caught - RefreshLiveAsync only ever looks at matches
    // that are currently live or were previously InProgress in our DB, so a match that goes straight
    // from Upcoming to Postponed at the provider is otherwise invisible to it (see
    // ApiFootballMatchSyncService.RecheckStaleUpcomingAsync). 60 minutes gives real kickoffs plenty
    // of time to show up via the live poll before being treated as suspicious.
    public int StaleUpcomingMinutes { get; set; } = 60;

    // Blank by default (checked-in secret-like placeholder, same convention as
    // Highlightly:AlertEmail) - set via ApiFootball__AlertEmail on the VM. Shared by two distinct
    // alert sources: ApiFootballMatchSyncService's stale-InProgress-match detector (§12.4-style,
    // a stuck-match watchdog) and ApiFootballClient's call-count/quota alerting below (a call-volume
    // tracker) - different concerns, same destination address, no reason for two separate settings.
    public string AlertEmail { get; set; } = "";
}
