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
}
