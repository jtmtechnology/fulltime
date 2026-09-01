namespace FullTime.Api.Betting;

public class BettingOptions
{
    public const string SectionName = "Betting";

    public decimal StartingBalance { get; set; } = 100;
    public int SweepIntervalSeconds { get; set; } = 30;

    // Flat weekly top-up, applied to every league membership. Bumps StartingBalance by the same
    // amount as Balance so it stays profit-neutral (see LeagueMembership.StartingBalance's own
    // comment on why Profit = Balance - StartingBalance must never move for a reason other than
    // actual betting outcomes).
    public decimal WeeklyTopUpAmount { get; set; } = 10;

    // Checked often enough that the top-up lands close to its 9pm UTC target (see
    // WeeklyTopUpService.TopUpHourUtc) rather than up to an hour late.
    public int WeeklyTopUpCheckIntervalMinutes { get; set; } = 15;

    // Flat bonus credited when the Daily Spinner streak reaches day 7 - same profit-neutral rule as
    // WeeklyTopUpAmount (see SpinService.CreditAllMembershipsAsync).
    public decimal SpinStreakBonusAmount { get; set; } = 100;

    // The local hour (0-23, per each user's own UtcOffsetMinutes) after which SpinReminderService
    // starts nudging anyone who hasn't spun yet today.
    public int SpinReminderHour { get; set; } = 16;

    // Checked often enough that the reminder lands reasonably close to SpinReminderHour rather than
    // up to an hour late, same reasoning as WeeklyTopUpCheckIntervalMinutes.
    public int SpinReminderCheckIntervalMinutes { get; set; } = 15;
}
