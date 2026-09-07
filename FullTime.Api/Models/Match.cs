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

    // Last time BetBuilderSyncService.ResolveMatchEventsAsync fetched this match's full Highlightly
    // event timeline (goals/cards/subs) - once a finished match has this set, its MatchEvents rows
    // are permanent and it's never refetched. Separate from FirstGoalScorerSide (derived from the
    // same fetch) since a 0-0 match resolves that immediately without needing events, but still
    // gets its own events fetch now for the card/substitution timeline.
    public DateTime? EventsFetchedAt { get; set; }

    public List<MatchEvent> Events { get; set; } = [];
}
