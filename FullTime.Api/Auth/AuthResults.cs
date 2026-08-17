namespace FullTime.Api.Auth;

public enum RegisterOutcome
{
    Success,
    EmailTaken,
}

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    EmailNotVerified,
}

public enum VerifyEmailOutcome
{
    Success,
    InvalidOrExpiredToken,
}

public enum ResetPasswordOutcome
{
    Success,
    InvalidOrExpiredToken,
}

public record RegisterResult(RegisterOutcome Outcome, Guid? UserId = null);

public record LoginResult(LoginOutcome Outcome, string? Token = null, Guid? UserId = null, string? Name = null);
