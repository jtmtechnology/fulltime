using FullTime.Api.Models;

namespace FullTime.Api.Leagues;

public enum JoinLeagueOutcome
{
    Success,
    InvalidCode,
    AlreadyMember,
    MaxLeaguesReached,
}

public record JoinLeagueResult(JoinLeagueOutcome Outcome, League? League = null);

public enum CreateLeagueOutcome
{
    Success,
    MaxLeaguesReached,
    ProfaneName,
    NameTaken,
}

public record CreateLeagueResult(CreateLeagueOutcome Outcome, League? League = null);

public enum InviteOutcome
{
    Success,
    LeagueNotFound,
    NotMember,
}
