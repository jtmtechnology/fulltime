namespace FullTime.Api.Spin;

public enum SpinOutcome
{
    Success,
    AlreadySpunToday,
}

public record SpinResult(
    SpinOutcome Outcome,
    int WinningIndex = 0,
    int Streak = 0,
    decimal? MysteryCashAmount = null,
    decimal? StreakBonusAmount = null,
    string? BoostLabel = null);
