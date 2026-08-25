using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FullTime.Api.Auth;

public class AuthService(
    AppDbContext db,
    IOptions<JwtOptions> jwtOptions,
    IOptions<BettingOptions> bettingOptions,
    IEmailSender emailSender,
    ILogger<AuthService> logger)
{
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    public async Task<RegisterResult> RegisterAsync(string name, string email, string password, string baseUrl, CancellationToken ct = default)
    {
        if (ProfanityFilter.ContainsProfanity(name))
        {
            return new RegisterResult(RegisterOutcome.ProfaneName);
        }

        var normalizedEmail = Normalize(email);

        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct))
        {
            return new RegisterResult(RegisterOutcome.EmailTaken);
        }

        var token = GenerateToken();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            EmailVerified = false,
            EmailVerificationToken = token,
            EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(VerificationTokenLifetime),
            Balance = bettingOptions.Value.StartingBalance,
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        await SendVerificationEmailAsync(user, baseUrl, ct);

        return new RegisterResult(RegisterOutcome.Success, user.Id);
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var normalizedEmail = Normalize(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            logger.LogWarning("Failed login attempt for {Email}", normalizedEmail);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (!user.EmailVerified)
        {
            return new LoginResult(LoginOutcome.EmailNotVerified);
        }

        return new LoginResult(LoginOutcome.Success, GenerateJwt(user), user.Id, user.Name);
    }

    public async Task<VerifyEmailOutcome> VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.EmailVerificationToken == token, ct);

        if (user is null || user.EmailVerificationTokenExpiresAt is null || user.EmailVerificationTokenExpiresAt < DateTime.UtcNow)
        {
            return VerifyEmailOutcome.InvalidOrExpiredToken;
        }

        user.EmailVerified = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpiresAt = null;
        await db.SaveChangesAsync(ct);

        return VerifyEmailOutcome.Success;
    }

    public async Task ResendVerificationAsync(string email, string baseUrl, CancellationToken ct = default)
    {
        var normalizedEmail = Normalize(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null || user.EmailVerified)
        {
            return;
        }

        user.EmailVerificationToken = GenerateToken();
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(VerificationTokenLifetime);
        await db.SaveChangesAsync(ct);

        await SendVerificationEmailAsync(user, baseUrl, ct);
    }

    public async Task ForgotPasswordAsync(string email, string baseUrl, CancellationToken ct = default)
    {
        var normalizedEmail = Normalize(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null)
        {
            return;
        }

        user.PasswordResetToken = GenerateToken();
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.Add(ResetTokenLifetime);
        await db.SaveChangesAsync(ct);

        var link = $"{baseUrl}/reset-password.html?token={user.PasswordResetToken}";
        await emailSender.SendAsync(user.Email, "Reset your FullTime password",
            $"Click to reset your password: {link}", ct);
    }

    public async Task<ResetPasswordOutcome> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.PasswordResetToken == token, ct);

        if (user is null || user.PasswordResetTokenExpiresAt is null || user.PasswordResetTokenExpiresAt < DateTime.UtcNow)
        {
            return ResetPasswordOutcome.InvalidOrExpiredToken;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;
        await db.SaveChangesAsync(ct);

        return ResetPasswordOutcome.Success;
    }

    private async Task SendVerificationEmailAsync(User user, string baseUrl, CancellationToken ct)
    {
        var link = $"{baseUrl}/verify-email.html?token={user.EmailVerificationToken}";
        await emailSender.SendAsync(user.Email, "Verify your FullTime email",
            $"Click to verify your email: {link}", ct);
    }

    private string GenerateJwt(User user)
    {
        var options = jwtOptions.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("name", user.Name),
            new Claim("email", user.Email),
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string GenerateToken() => RandomNumberGenerator.GetHexString(64);
}
