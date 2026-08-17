using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record SelectionRequest(Guid MatchId, string Pick);
public record PlaceBetRequest(decimal Stake, List<SelectionRequest> Selections, Guid? LeagueId);

public record BetSelectionDto(
    Guid MatchId, string HomeTeam, string AwayTeam, string? HomeLogoUrl, string? AwayLogoUrl,
    DateTime KickoffTime, string Pick, decimal OddsAtPlacement, string Outcome);
public record BetDto(Guid Id, decimal Stake, decimal CombinedOdds, decimal PotentialReturn, string Status,
    DateTime PlacedAt, DateTime? SettledAt, Guid? LeagueId, string? LeagueName, List<BetSelectionDto> Selections);

[ApiController]
[Route("api/bets")]
[Authorize]
public class BetsController(AppDbContext db, BetService betService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PlaceBet([FromBody] PlaceBetRequest request, CancellationToken ct)
    {
        var selections = new List<(Guid MatchId, MatchOutcome Pick)>();
        foreach (var s in request.Selections)
        {
            if (!Enum.TryParse<MatchOutcome>(s.Pick, ignoreCase: true, out var pick))
            {
                return BadRequest(new { error = $"Invalid pick '{s.Pick}' — expected Home, Draw, or Away." });
            }
            selections.Add((s.MatchId, pick));
        }

        var result = await betService.PlaceBetAsync(CurrentUserId, request.Stake, selections, request.LeagueId, ct);

        if (result.Outcome != PlaceBetOutcome.Success)
        {
            var error = result.Outcome switch
            {
                PlaceBetOutcome.NoSelections => "A bet needs at least one selection.",
                PlaceBetOutcome.DuplicateMatch => "Each match can only be picked once per bet.",
                PlaceBetOutcome.InvalidStake => "Stake must be greater than zero.",
                PlaceBetOutcome.InsufficientBalance => "Stake exceeds your current balance.",
                PlaceBetOutcome.MatchNotAvailable => "One or more selections are no longer available (kicked off, finished, or missing odds).",
                PlaceBetOutcome.InvalidLeague => "You're not a member of that league.",
                _ => "Could not place bet.",
            };
            return BadRequest(new { error });
        }

        var dto = await LoadBetDtoAsync(result.Bet!.Id, ct);
        return Created(string.Empty, dto);
    }

    [HttpGet("me")]
    public async Task<ActionResult<List<BetDto>>> GetMyBets(CancellationToken ct)
    {
        var bets = await db.Bets
            .Where(b => b.UserId == CurrentUserId)
            .OrderByDescending(b => b.PlacedAt)
            .Select(b => new BetDto(
                b.Id, b.Stake, b.CombinedOdds, b.PotentialReturn, b.Status.ToString(), b.PlacedAt, b.SettledAt,
                b.LeagueId, b.LeagueId != null ? b.League!.Name : null,
                b.Selections.Select(s => new BetSelectionDto(
                    s.MatchId,
                    s.Match!.HomeTeam,
                    s.Match!.AwayTeam,
                    s.Match!.HomeTeamId > 0 ? $"https://images.fotmob.com/image_resources/logo/teamlogo/{s.Match!.HomeTeamId}_large.png" : null,
                    s.Match!.AwayTeamId > 0 ? $"https://images.fotmob.com/image_resources/logo/teamlogo/{s.Match!.AwayTeamId}_large.png" : null,
                    s.Match!.KickoffTime,
                    s.Pick.ToString(),
                    s.OddsAtPlacement,
                    s.Outcome.ToString())).ToList()))
            .ToListAsync(ct);

        return Ok(bets);
    }

    private async Task<BetDto?> LoadBetDtoAsync(Guid betId, CancellationToken ct) =>
        await db.Bets
            .Where(b => b.Id == betId)
            .Select(b => new BetDto(
                b.Id, b.Stake, b.CombinedOdds, b.PotentialReturn, b.Status.ToString(), b.PlacedAt, b.SettledAt,
                b.LeagueId, b.LeagueId != null ? b.League!.Name : null,
                b.Selections.Select(s => new BetSelectionDto(
                    s.MatchId,
                    s.Match!.HomeTeam,
                    s.Match!.AwayTeam,
                    s.Match!.HomeTeamId > 0 ? $"https://images.fotmob.com/image_resources/logo/teamlogo/{s.Match!.HomeTeamId}_large.png" : null,
                    s.Match!.AwayTeamId > 0 ? $"https://images.fotmob.com/image_resources/logo/teamlogo/{s.Match!.AwayTeamId}_large.png" : null,
                    s.Match!.KickoffTime,
                    s.Pick.ToString(),
                    s.OddsAtPlacement,
                    s.Outcome.ToString())).ToList()))
            .FirstOrDefaultAsync(ct);

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
