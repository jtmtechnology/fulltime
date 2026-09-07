using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

// Shared between MyBets.razor and Leaderboard.razor's Live Bets section — both render the same
// leg/pick shape and shouldn't drift on how a pick reads.
public static class BetDisplay
{
    public static string StatusClass(string status) => status switch
    {
        "Won" or "Correct" => "live",
        "Lost" or "Incorrect" => "error",
        _ => "muted",
    };

    public static string PickLabel(BetLegPickDto pick, string homeTeam, string awayTeam) => pick.MarketType switch
    {
        "MatchResult" => pick.Side switch { "Home" => "Home win", "Away" => "Away win", _ => "Draw" },
        "OverUnder" => $"{pick.Side} {pick.Line:0.##}",
        "BothTeamsToScore" => pick.Side == "Yes" ? "BTTS Yes" : "BTTS No",
        "CorrectScore" => $"{pick.PredictedHomeScore} - {pick.PredictedAwayScore}",
        "FirstTeamToScore" => $"First to score: {pick.Side switch { "Home" => homeTeam, "Away" => awayTeam, _ => "None" }}",
        "PlayerGoalscorerAnytime" => $"{pick.PlayerName} to score anytime",
        "PlayerCard" => $"{pick.PlayerName} to be booked",
        "PlayerShotsOnTarget" => $"{pick.PlayerName} {LinePlusLabel(pick.Line)} shots on target",
        "PlayerAssists" => $"{pick.PlayerName} {LinePlusLabel(pick.Line)} assists",
        _ => pick.Side ?? "",
    };

    // "Over 1.5" reads as "2+" - matches BetBuilder.razor's LineLabel convention for the same lined
    // player-prop markets (a half-line "Over 1.5" means 2 or more, shown as a whole number).
    private static string LinePlusLabel(decimal? line) => line is { } l ? $"{(int)(l + 1m)}+" : "";
}
