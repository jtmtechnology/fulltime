namespace FullTime.Api.Models;

// One row per player who touched a finished match, sourced from API-Football's
// fixtures/players?fixture={id} (confirmed real via a spike against a finished fixture 2026-09-06 —
// goals/assists/shots-on-target/yellow-cards all populated). Powers settlement for
// PlayerGoalscorerAnytime/PlayerCard/PlayerShotsOnTarget/PlayerAssists picks — see
// SettlementService.IsPickCorrect. Team is Home/Away, matching Match.HomeTeamId/AwayTeamId — never
// Draw/Over/Under/etc., but reuses SelectionSide rather than a two-value enum since nothing else in
// the codebase has a dedicated home/away-only type.
public class MatchPlayerStat
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public required string PlayerName { get; set; }
    public SelectionSide Team { get; set; }

    public int Goals { get; set; }
    public int Assists { get; set; }
    public int ShotsOnTarget { get; set; }
    public int YellowCards { get; set; }
}
