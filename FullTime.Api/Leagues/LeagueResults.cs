using FullTime.Api.Models;

namespace FullTime.Api.Leagues;

public enum JoinLeagueOutcome
{
    Success,
    InvalidCode,
    AlreadyMember,
}

public record JoinLeagueResult(JoinLeagueOutcome Outcome, League? League = null);
