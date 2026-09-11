namespace FullTime.Api.Models;

public class Match
{
    public Guid Id { get; set; }

    // Highlightly's own match/team/league IDs, used directly (no second provider to reconcile
    // against — see BetBuilderSyncService, which used to fuzzy-match team names/dates against a
    // separate primary provider before Highlightly became the only data source).
    public required string ExternalId { get; set; }
    public long LeagueId { get; set; }

    // API-Football's own round label for knockout competitions (e.g. "1st Round Qualifying",
    // "3rd Round Proper") - null for league competitions where it isn't meaningful. Only actually
    // used to filter FA Cup's early non-league rounds out of sync entirely - see
    // ApiFootballMatchSyncService.IsEligibleFaCupRound.
    public string? Round { get; set; }
    public required string HomeTeam { get; set; }
    public required string AwayTeam { get; set; }
    public long HomeTeamId { get; set; }
    public long AwayTeamId { get; set; }

    // Highlightly gives a logo per team, but not reliably for every team (smaller/lower-profile
    // clubs often have none) — stored as given rather than derived from HomeTeamId/AwayTeamId,
    // unlike league logos which follow a confirmed stable URL pattern (see LeagueCatalog.LogoUrl
    // client-side).
    public string? HomeTeamLogoUrl { get; set; }
    public string? AwayTeamLogoUrl { get; set; }

    public DateTime KickoffTime { get; set; }
    public MatchStatus Status { get; set; }
    public MatchOutcome? Result { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    // Current match minute while Status == InProgress — Highlightly's own "clock" value, null
    // before kickoff. Not meaningful once Finished (stays at whatever it last was, e.g. 90) — the
    // client shows "FT" instead of a stale minute once Status flips.
    public int? Minute { get; set; }

    // Stoppage/added time on top of Minute (e.g. Minute=90, AddedTimeMinutes=3 during "90+3") -
    // only ever populated by API-Football (its fixtures' status.extra field); Highlightly has no
    // equivalent, so this stays null under that provider. Same "not meaningful once Finished"
    // caveat as Minute above.
    public int? AddedTimeMinutes { get; set; }

    // True only during the pause between halves — Highlightly's own description says so explicitly
    // (checked case-insensitively for "half" rather than an exact string match, since the confirmed
    // in-play vocabulary so far — "Not started"/"Finished"*/"Postponed" — didn't include a live
    // example to pin the exact wording down; verify once a real match reaches half time). Kept
    // separate from MatchStatus rather than adding a new enum value there, since Status ==
    // InProgress is still correct for settlement/betting purposes during a half-time break.
    public bool IsHalfTime { get; set; }

    public List<OddsSnapshot> OddsSnapshots { get; set; } = [];
    public List<BetLeg> BetLegs { get; set; } = [];
    public List<BetBuilderMarket> BetBuilderMarkets { get; set; } = [];

    // Null until resolved: set to Home/Away once the match's first goal (if any) is confirmed via
    // Highlightly's event timeline, or straight to None once the final score is 0-0 (no external
    // call needed for that case). Needed to settle MarketType.FirstTeamToScore picks, which can't
    // be derived from the final score alone. See BetBuilderSyncService.ResolveMatchEventsAsync.
    public SelectionSide? FirstGoalScorerSide { get; set; }

    // Corner kicks summed across both teams from API-Football's fixtures/statistics endpoint — null
    // until a finished match's player-stats resolution pass has run. Settles MarketType.TotalCorners
    // the same way OverUnder settles off HomeScore+AwayScore (see SettlementService.IsPickCorrect).
    public int? TotalCorners { get; set; }

    // Set once ApiFootballPlayerStatsService has attempted (successfully or not) to fetch and store
    // this match's MatchPlayerStats/TotalCorners — a timestamp rather than a bool so a match whose
    // provider data never backfills (some lower-league/qualifying fixtures never get one) can still
    // age out of being re-queried every tick forever, same reasoning as FirstGoalScorerSide's cutoff
    // in BetBuilderSyncService.ResolveMatchEventsAsync.
    public DateTime? PlayerStatsResolvedAt { get; set; }

    public List<MatchPlayerStat> PlayerStats { get; set; } = [];

    // Last time OddsApiMarketService.EnsureBetBuilderMarketsFreshAsync attempted a full market pull
    // for this match — set even on a cache miss with no the-odds-api event found, so a TTL check has
    // something to compare against without depending on BetBuilderMarket rows existing (an unpriced
    // match has none). Drives that method's TTL gating; OddsSnapshot.FetchedAt already serves the
    // same purpose for the cheaper h2h-only refresh (MatchesController.Upcoming).
    public DateTime? OddsLastFetchedAt { get; set; }

    // Last time PlayerPropsService attempted a fetch for this match (any of its tracked player-prop
    // markets) - separate from OddsLastFetchedAt (that one belongs to the dormant full OddsApi
    // cutover) since this is an independent, always-on EPL-only feature that runs regardless of
    // Providers:MarketsSource.
    public DateTime? PlayerPropsFetchedAt { get; set; }

    // Last time the event timeline (goals/cards/subs) was fetched for this match at all - while
    // InProgress this updates every ~30s (BetBuilderSyncService.RefreshLiveMatchEventsAsync, same
    // cadence as the live score/clock sync) so Match Summary stays current during the game. Once a
    // match reaches Finished, EventsFinalizedAt below takes over and this stops changing.
    public DateTime? EventsFetchedAt { get; set; }

    // Set once BetBuilderSyncService.ResolveMatchEventsAsync has done the final, authoritative
    // post-match events fetch - separate from EventsFetchedAt so a match that was refreshed live
    // right up to the final whistle still gets exactly one more fetch afterwards (catching a very
    // late goal/card the last live tick might have just missed), rather than treating its last live
    // timestamp as good enough forever. FirstGoalScorerSide is derived from this same fetch; a 0-0
    // match resolves it immediately without needing events, but still gets its own fetch for the
    // card/substitution timeline.
    public DateTime? EventsFinalizedAt { get; set; }

    public List<MatchEvent> Events { get; set; } = [];

    // Last time ApiFootballOddsService.EnsureBetBuilderMarketsFreshAsync attempted a fetch for this
    // match - same "stamp even on a miss" reasoning as OddsLastFetchedAt, kept as a separate field
    // since the two odds sources have independent TTL-gating and aren't both active at once
    // (Providers:MarketsSource picks one).
    public DateTime? ApiFootballOddsLastFetchedAt { get; set; }

    // Per-team corner/card counts, split out from the existing summed TotalCorners - populated
    // alongside it in ApiFootballSettlementSupportService.ResolvePlayerStatsAsync's existing
    // fixtures/statistics fetch (no extra API call). Settle MarketType.TeamCorners/TeamCards.
    public int? HomeCorners { get; set; }
    public int? AwayCorners { get; set; }
    public int? HomeCards { get; set; }
    public int? AwayCards { get; set; }
}
