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

    // Settled via API-Football's player stats (MatchPlayerStat.ShotsOnTarget) - the one player-prop
    // market Highlightly can't settle at all (checked its events/statistics/lineups endpoints for
    // real 2026-09-07, no per-player shots data anywhere), so API-Football stays wired in
    // specifically for this plus PlayerShots below, on top of its squad-lookup use in
    // PlayerPropsService. Briefly retired the same day, restored once the owner confirmed keeping
    // API-Football for shots settlement specifically was the intent.
    PlayerShotsOnTarget,
    PlayerAssists,

    // Settled the same way as PlayerCard (a Highlightly "Red Card" event for this player) - see
    // SettlementService.IsPickCorrect.
    PlayerRedCard,

    // Total shots (not just on target) - same API-Football settlement path as PlayerShotsOnTarget,
    // via MatchPlayerStat.TotalShots.
    PlayerShots,
}
