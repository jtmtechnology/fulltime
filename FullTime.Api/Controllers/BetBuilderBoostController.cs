using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Betting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FullTime.Api.Controllers;

public record BetBuilderBoostStatusDto(
    bool Available, Guid? MatchId, string? HomeTeam, string? AwayTeam, decimal Percent, int MinSelections,
    DateTime? KickoffTime, string? HomeLogoUrl, string? AwayLogoUrl, long? LeagueId);

[ApiController]
[Route("api/betbuilderboost")]
[Authorize]
public class BetBuilderBoostController(BetBuilderBoostService boostService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<BetBuilderBoostStatusDto>> GetStatus(CancellationToken ct)
    {
        var status = await boostService.GetStatusAsync(CurrentUserId, ct);
        return Ok(new BetBuilderBoostStatusDto(
            status.Available, status.MatchId, status.HomeTeam, status.AwayTeam, status.Percent, status.MinSelections,
            status.KickoffTime, status.HomeLogoUrl, status.AwayLogoUrl, status.LeagueId));
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
