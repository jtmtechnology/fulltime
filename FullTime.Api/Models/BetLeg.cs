namespace FullTime.Api.Models;

// One match's contribution to a Bet. A classic single pick is a BetLeg with exactly one
// BetLegPick; a same-game multi (bet builder) is a BetLeg with two or more picks whose odds
// were already multiplied together into this leg's own OddsAtPlacement.
public class BetLeg
{
    public Guid Id { get; set; }
    public Guid BetId { get; set; }
    public Bet? Bet { get; set; }

    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public decimal OddsAtPlacement { get; set; }
    public SelectionOutcome Outcome { get; set; }

    public List<BetLegPick> Picks { get; set; } = [];
}
