using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

// Shared between MatchCard.razor, MatchSummarySheet.razor and BetList.razor so a finished match's
// "FT"/"AET" label and penalty-shootout score read the same everywhere rather than drifting.
public static class MatchDisplay
{
    public static string FinishedLabel(UpcomingMatchDto match) => FinishedLabel(match.WentToExtraTime);

    public static string FinishedLabel(bool wentToExtraTime) => wentToExtraTime ? "AET" : "FT";

    public static string? PenaltyScoreText(UpcomingMatchDto match) =>
        PenaltyScoreText(match.HomePenalties, match.AwayPenalties);

    public static string? PenaltyScoreText(int? homePenalties, int? awayPenalties) =>
        homePenalties is { } home && awayPenalties is { } away ? $"{home}-{away} pens" : null;
}
