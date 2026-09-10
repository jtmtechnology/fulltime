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
    // MatchSyncDaysAhead. PRO tier (7,500 req/day, 300 req/min) comfortably supports this at the
    // daily FixtureDiscoveryIntervalMinutes cadence.
    public int MatchSyncDaysAhead { get; set; } = 7;
    public int FixtureDiscoveryIntervalMinutes { get; set; } = 1440;

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
    // match count), this is genuinely one call PER live match PER tick - deliberately set slower
    // than Highlightly's ~30s equivalent to keep worst-case cost (several concurrent live matches
    // on a busy Saturday) further from Pro's 7,500/day ceiling. Settlement itself doesn't depend on
    // this cadence - only the live display does - so a slower value here is a pure quota/freshness
    // tradeoff, safe to raise once the account is upgraded.
    public int LiveEventsRefreshIntervalSeconds { get; set; } = 45;

    // Bet365's api-sports.io bookmaker ID (confirmed live 2026-09-10: {"id":8,"name":"Bet365"}) -
    // same choice HighlightlyOptions.BookmakerName ("bet365") already made, for the richest
    // coverage of the bookmakers available on this plan.
    public long OddsBookmakerId { get; set; } = 8;

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

    // Blank by default (checked-in secret-like placeholder, same convention as
    // Highlightly:AlertEmail) - set via ApiFootball__AlertEmail on the VM.
    public string AlertEmail { get; set; } = "";
}
