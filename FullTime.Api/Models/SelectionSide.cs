namespace FullTime.Api.Models;

// Shared across market types rather than one enum per type: MatchResult uses Home/Draw/Away,
// OverUnder uses Over/Under, BothTeamsToScore uses Yes/No, FirstTeamToScore uses Home/Away/None.
// CorrectScore doesn't use Side at all — see BetLegPick.PredictedHomeScore/PredictedAwayScore.
public enum SelectionSide
{
    Home,
    Draw,
    Away,
    Over,
    Under,
    Yes,
    No,
    None,
}
