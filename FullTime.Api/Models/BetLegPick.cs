namespace FullTime.Api.Models;

public class BetLegPick
{
    public Guid Id { get; set; }
    public Guid BetLegId { get; set; }
    public BetLeg? BetLeg { get; set; }

    public MarketType MarketType { get; set; }

    // Null for MarketType.MatchResult, BothTeamsToScore, and CorrectScore. Half-lines only
    // (1.5/2.5/...) for OverUnder — goals are integers, so a half-line can never push, which keeps
    // SelectionOutcome binary with no Void/Refund state needed.
    public decimal? Line { get; set; }

    // Null only for MarketType.CorrectScore, which uses PredictedHomeScore/PredictedAwayScore
    // instead — there's no meaningful "side" for one specific final score.
    public SelectionSide? Side { get; set; }

    // Only set for MarketType.CorrectScore.
    public int? PredictedHomeScore { get; set; }
    public int? PredictedAwayScore { get; set; }

    public decimal OddsAtPlacement { get; set; }
    public SelectionOutcome Outcome { get; set; }
}
