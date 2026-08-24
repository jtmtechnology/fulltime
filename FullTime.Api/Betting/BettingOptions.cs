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
    public int WeeklyTopUpCheckIntervalMinutes { get; set; } = 60;
}
