namespace FullTime.App.Shared.Models;

// Request/response shapes mirroring FullTime.Api's controllers exactly (System.Text.Json's
// default camelCase handling matches what the API already emits — no serializer config needed).

public record RegisterRequest(string Name, string Email, string Password);
public record RegisterResponse(Guid UserId);

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, Guid UserId, string Name);

public record VerifyEmailRequest(string Token);
public record ResendVerificationRequest(string Email);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);

public record MeDto(Guid Id, string Name, string Email, bool EmailVerified, decimal Balance, DateTime CreatedAt);
public record UpdateProfileRequest(string Name);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MessageResponse(string? Message, string? Error, string? Reason);

public record RegisterDeviceRequest(string Token, string Platform);

public record UpcomingMatchDto(
    Guid Id,
    long LeagueId,
    string HomeTeam,
    string AwayTeam,
    string? HomeLogoUrl,
    string? AwayLogoUrl,
    DateTime KickoffTime,
    string Status,
    int? HomeScore,
    int? AwayScore,
    int? Minute,
    bool IsHalfTime,
    decimal? HomeOdds,
    decimal? DrawOdds,
    decimal? AwayOdds,
    string? Bookmaker,
    string? BookmakerLogoUrl,
    bool BetBuilderAvailable);

public record ConfigResponse(int RefreshIntervalSeconds);

public record PickRequest(string MarketType, decimal? Line, string? Side, int? PredictedHomeScore = null, int? PredictedAwayScore = null);
public record LegRequest(Guid MatchId, List<PickRequest> Picks);
public record PlaceBetRequest(decimal Stake, List<LegRequest> Legs, Guid? LeagueId);

public record BetLegPickDto(
    string MarketType, decimal? Line, string? Side, int? PredictedHomeScore, int? PredictedAwayScore,
    decimal OddsAtPlacement, string Outcome);
public record BetLegDto(
    Guid MatchId, string HomeTeam, string AwayTeam, string? HomeLogoUrl, string? AwayLogoUrl,
    DateTime KickoffTime, decimal OddsAtPlacement, string Outcome, List<BetLegPickDto> Picks);
public record BetDto(Guid Id, decimal Stake, decimal CombinedOdds, decimal PotentialReturn, string Status,
    DateTime PlacedAt, DateTime? SettledAt, Guid? LeagueId, string? LeagueName, List<BetLegDto> Legs);

public record BetBuilderMarketDto(string MarketType, decimal? Line, string? Side, int? PredictedHomeScore, int? PredictedAwayScore, decimal Price);
public record BetBuilderMarketsResponse(bool Available, List<BetBuilderMarketDto> Markets, string? Bookmaker, string? BookmakerLogoUrl);

public record LeaderboardEntryDto(Guid UserId, string Name, decimal Balance, decimal Profit);

public record CreateLeagueRequest(string Name);
public record JoinLeagueRequest(string InviteCode);
public record LeagueSummaryDto(
    Guid Id, string Name, string InviteCode, int MemberCount, DateTime CreatedAt, bool IsOwner,
    decimal Balance, decimal Profit);
