namespace FullTime.Api.Models;

public class BetSelection
{
    public Guid Id { get; set; }
    public Guid BetId { get; set; }
    public Bet? Bet { get; set; }

    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public MatchOutcome Pick { get; set; }
    public decimal OddsAtPlacement { get; set; }
    public SelectionOutcome Outcome { get; set; }
}
