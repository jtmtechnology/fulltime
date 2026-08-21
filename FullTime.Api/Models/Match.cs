namespace FullTime.Api.Models;

public class Match
{
    public Guid Id { get; set; }
    public required string ExternalId { get; set; }
    public long LeagueId { get; set; }
    public required string HomeTeam { get; set; }
    public required string AwayTeam { get; set; }
    public long HomeTeamId { get; set; }
    public long AwayTeamId { get; set; }
    public DateTime KickoffTime { get; set; }
    public MatchStatus Status { get; set; }
    public MatchOutcome? Result { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public List<OddsSnapshot> OddsSnapshots { get; set; } = [];
    public List<BetLeg> BetLegs { get; set; } = [];
    public List<BetBuilderMarket> BetBuilderMarkets { get; set; } = [];

    // Highlightly's own IDs for this match, once BetBuilderSyncService has matched it — HomeTeamId/
    // AwayTeamId are Highlightly's team IDs (a different ID space from our own HomeTeamId/AwayTeamId
    // above), kept so First-Team-To-Score resolution can compare a goal event's team id directly
    // without re-running fuzzy name matching. LeagueId is which of a shared UEFA-qualifying pool's
    // possible Highlightly leagues this particular match was actually found under.
    public long? HighlightlyMatchId { get; set; }
    public int? HighlightlyLeagueId { get; set; }
    public long? HighlightlyHomeTeamId { get; set; }
    public long? HighlightlyAwayTeamId { get; set; }

    // Null until resolved: set to Home/Away once the match's first goal (if any) is confirmed via
    // Highlightly's event timeline, or straight to None once the final score is 0-0 (no external
    // call needed for that case). Needed to settle MarketType.FirstTeamToScore picks, which can't
    // be derived from the final score alone. See BetBuilderSyncService.ResolveFirstGoalScorersAsync.
    public SelectionSide? FirstGoalScorerSide { get; set; }
}
