using FullTime.Api.Models;

namespace FullTime.Api.Betting;

public enum PlaceBetOutcome
{
    Success,
    NoSelections,
    DuplicateMatch,
    InvalidStake,
    InsufficientBalance,
    MatchNotAvailable,
    InvalidLeague,
}

public record PlaceBetResult(PlaceBetOutcome Outcome, Bet? Bet = null);
