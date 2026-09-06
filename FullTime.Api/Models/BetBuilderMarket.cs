namespace FullTime.Api.Models;

// Versioned snapshot of one priced outcome for a match, same shape/intent as OddsSnapshot — but
// one row per outcome (not a fixed pair of price columns), so it scales to CorrectScore's 50+
// distinct scorelines as easily as OverUnder/BothTeamsToScore/FirstTeamToScore's smaller outcome
// sets. Line+Side apply to OverUnder/BothTeamsToScore/FirstTeamToScore; PredictedHomeScore/
// PredictedAwayScore apply to CorrectScore only.
public class BetBuilderMarket
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public MarketType MarketType { get; set; }
    public decimal? Line { get; set; }
    public SelectionSide? Side { get; set; }
    public int? PredictedHomeScore { get; set; }
    public int? PredictedAwayScore { get; set; }

    public decimal Price { get; set; }
    public DateTime FetchedAt { get; set; }

    // Null for match-level markets (everything Highlightly ever priced, plus TotalCorners) — set
    // for the-odds-api's per-player markets (PlayerGoalscorerAnytime/PlayerCard/
    // PlayerShotsOnTarget/PlayerAssists), which two different players can both be priced under the
    // same MarketType/Line/Side — see BetService.GetCurrentOddsAsync, which needs PlayerName to
    // disambiguate those lookups.
    public string? PlayerName { get; set; }

    // "Home"/"Away" — descriptive metadata for display, not a settlement selection (Side already
    // carries Over/Under/Yes/No). Null for match-level markets.
    public string? Team { get; set; }
}
