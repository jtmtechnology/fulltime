using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Spin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FullTime.Api.Controllers;

public record SpinStatusDto(bool CanSpin, int Streak, decimal? PendingBoostMultiplier, string? PendingBoostLabel);
public record SpinResultDto(int WinningIndex, int Streak, decimal? MysteryCashAmount, decimal? StreakBonusAmount, string? BoostLabel);

[ApiController]
[Route("api/spin")]
[Authorize]
public class SpinController(SpinService spinService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<SpinStatusDto>> GetStatus(CancellationToken ct)
    {
        var (canSpin, streak, boostMultiplier, boostLabel) = await spinService.GetStatusAsync(CurrentUserId, ct);
        return Ok(new SpinStatusDto(canSpin, streak, boostMultiplier, boostLabel));
    }

    [HttpPost]
    public async Task<IActionResult> Spin(CancellationToken ct)
    {
        var result = await spinService.SpinAsync(CurrentUserId, ct);
        if (result.Outcome == SpinOutcome.AlreadySpunToday)
        {
            return BadRequest(new { error = "You've already spun today — come back tomorrow." });
        }

        return Ok(new SpinResultDto(
            result.WinningIndex, result.Streak, result.MysteryCashAmount, result.StreakBonusAmount, result.BoostLabel));
    }

    // TEMP for testing only - see SpinService.ResetForTestingAsync. Remove this endpoint (and its
    // call site in DailySpinner.razor's DismissResult) once testing is done.
    [HttpPost("reset-for-testing")]
    public async Task<IActionResult> ResetForTesting(CancellationToken ct)
    {
        await spinService.ResetForTestingAsync(CurrentUserId, ct);
        return Ok();
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
