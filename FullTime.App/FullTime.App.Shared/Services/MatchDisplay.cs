using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

// Shared between MatchCard.razor and MatchSummarySheet.razor so a finished match's "FT"/"AET"
// label and penalty-shootout score read the same in both places rather than drifting.
public static class MatchDisplay
{
    public static string FinishedLabel(UpcomingMatchDto match) => match.WentToExtraTime ? "AET" : "FT";

    public static string? PenaltyScoreText(UpcomingMatchDto match) =>
        match.HomePenalties is { } home && match.AwayPenalties is { } away ? $"{home}-{away} pens" : null;
}
