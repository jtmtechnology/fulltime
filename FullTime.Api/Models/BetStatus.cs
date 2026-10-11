namespace FullTime.Api.Models;

public enum BetStatus
{
    Pending,
    Won,
    Lost,

    // Every leg was voided - the stake goes back to the pool it came from.
    Void
}
