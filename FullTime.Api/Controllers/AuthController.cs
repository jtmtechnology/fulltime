using FullTime.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FullTime.Api.Controllers;

public record RegisterRequest(string Name, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record VerifyEmailRequest(string Token);
public record ResendVerificationRequest(string Email);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);

public record LoginResponse(string Token, Guid UserId, string Name);

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            !IsValidEmail(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return BadRequest(new { error = "Name, a valid email, and a password of at least 8 characters are required." });
        }

        var result = await authService.RegisterAsync(request.Name, request.Email, request.Password, BaseUrl, ct);

        return result.Outcome switch
        {
            RegisterOutcome.EmailTaken => Conflict(new { error = "That email is already registered." }),
            RegisterOutcome.ProfaneName => BadRequest(new { error = "That name isn't allowed — please choose another." }),
            _ => Created(string.Empty, new { userId = result.UserId }),
        };
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request.Email, request.Password, ct);

        return result.Outcome switch
        {
            LoginOutcome.Success => Ok(new LoginResponse(result.Token!, result.UserId!.Value, result.Name!)),
            LoginOutcome.EmailNotVerified => StatusCode(403, new { reason = "EmailNotVerified" }),
            _ => Unauthorized(new { error = "Invalid email or password." }),
        };
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        var outcome = await authService.VerifyEmailAsync(request.Token, ct);

        return outcome == VerifyEmailOutcome.Success
            ? Ok(new { verified = true })
            : BadRequest(new { error = "That verification link is invalid or has expired." });
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        await authService.ResendVerificationAsync(request.Email, BaseUrl, ct);
        return Ok(new { message = "If that account exists and isn't verified yet, a new verification email has been sent." });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await authService.ForgotPasswordAsync(request.Email, BaseUrl, ct);
        return Ok(new { message = "If that email is registered, a password reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return BadRequest(new { error = "Password must be at least 8 characters." });
        }

        var outcome = await authService.ResetPasswordAsync(request.Token, request.NewPassword, ct);

        return outcome == ResetPasswordOutcome.Success
            ? Ok(new { message = "Password reset — you can log in with your new password now." })
            : BadRequest(new { error = "That reset link is invalid or has expired." });
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var trimmed = email.Trim();
        try
        {
            return new System.Net.Mail.MailAddress(trimmed).Address == trimmed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
