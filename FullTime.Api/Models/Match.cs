namespace FullTime.Api.Models;

public class Match
{
    public Guid Id { get; set; }

    // Highlightly's own match/team/league IDs, used directly (no second provider to reconcile
    // against — see BetBuilderSyncService, which used to fuzzy-match team names/dates against a
    // separate primary provider before Highlightly became the only data source).
    public required string ExternalId { get; set; }
    public long LeagueId { get; set; }
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
    // be derived from the final score alone. See BetBuilderSyncService.ResolveFirstGoalScorersAsync.
    public SelectionSide? FirstGoalScorerSide { get; set; }
}
