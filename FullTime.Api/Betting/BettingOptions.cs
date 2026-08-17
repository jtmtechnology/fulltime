namespace FullTime.Api.Betting;

public class BettingOptions
{
    public const string SectionName = "Betting";

    public decimal StartingBalance { get; set; } = 1000;
    public int SweepIntervalSeconds { get; set; } = 30;
}
