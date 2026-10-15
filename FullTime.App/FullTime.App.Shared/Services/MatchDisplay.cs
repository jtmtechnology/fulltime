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

    // Same rules as MatchCard's LiveClockText: "HT", "63'", "90+3'", or null with no clock yet.
    public static string? LiveClock(int? minute, int? addedTimeMinutes, bool isHalfTime) => isHalfTime
        ? "HT"
        : minute is { } m
            ? addedTimeMinutes is { } added && added > 0 ? $"{m}+{added}'" : $"{m}'"
            : null;
}
