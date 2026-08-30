namespace FullTime.App.Shared.Models;

public sealed record SpinSegment(string Icon, string Label);

// Placeholder catalogue only, purely for the wheel UI - real prize values/weighting/redemption
// aren't wired up yet. Shared between the full Daily Spinner wheel and its banner's mini preview
// so both stay in sync once real prizes replace these placeholders.
public static class SpinSegments
{
    public static readonly SpinSegment[] All =
    [
        new("❓", "Mystery Cash"),
        new("🚀", "Bet Boost 25%"),
        new("⏳", "Try Again Tomorrow"),
        new("❓", "Mystery Cash"),
        new("2x", "2x Odds Boost"),
        new("🚀", "Bet Boost 50%"),
        new("❓", "Mystery Cash"),
        new("⏳", "Try Again Tomorrow"),
    ];
}
