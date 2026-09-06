namespace FullTime.Api.Models;

public enum MarketType
{
    MatchResult,
    OverUnder,
    BothTeamsToScore,
    CorrectScore,
    FirstTeamToScore,

    // Added for the API-Football/the-odds-api cutover — appended only, never reordered/renumbered
    // (this enum persists as a bare int with no reference table, confirmed via the AddBetBuilder
    // migration). Names match FullTime.App.Shared/Pages/BetBuilder.razor's client-only
    // PlayerPropMarketTypes strings exactly, so MarketType.ToString() needs no separate mapping
    // table on either end.
    TotalCorners,
    PlayerGoalscorerAnytime,
    PlayerCard,
    PlayerShotsOnTarget,
    PlayerAssists,
}
