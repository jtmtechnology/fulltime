using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

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
    DateTime PlacedAt, DateTime? SettledAt, Guid? LeagueId, string? LeagueName, string? BoostApplied, List<BetLegDto> Legs);

[ApiController]
[Route("api/bets")]
[Authorize]
public class BetsController(AppDbContext db, BetService betService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PlaceBet([FromBody] PlaceBetRequest request, CancellationToken ct)
    {
        var legs = new List<LegInput>();
        foreach (var legRequest in request.Legs)
        {
            var picks = new List<LegPickInput>();
            foreach (var pickRequest in legRequest.Picks)
            {
                if (!Enum.TryParse<MarketType>(pickRequest.MarketType, ignoreCase: true, out var marketType))
                {
                    return BadRequest(new { error = $"Invalid market type '{pickRequest.MarketType}'." });
                }

                SelectionSide? side = null;
                if (marketType == MarketType.CorrectScore)
                {
                    if (pickRequest.PredictedHomeScore is null || pickRequest.PredictedAwayScore is null)
                    {
                        return BadRequest(new { error = "Correct Score picks need both PredictedHomeScore and PredictedAwayScore." });
                    }
                }
                else if (!Enum.TryParse<SelectionSide>(pickRequest.Side, ignoreCase: true, out var parsedSide))
                {
                    return BadRequest(new { error = $"Invalid side '{pickRequest.Side}' — expected Home, Draw, Away, Over, Under, Yes, No, or None." });
                }
                else
                {
                    side = parsedSide;
                }

                picks.Add(new LegPickInput(marketType, pickRequest.Line, side, pickRequest.PredictedHomeScore, pickRequest.PredictedAwayScore));
            }

            legs.Add(new LegInput(legRequest.MatchId, picks));
        }

        var result = await betService.PlaceBetAsync(CurrentUserId, request.Stake, legs, request.LeagueId, ct);

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
                PlaceBetOutcome.NoLeagueSelected => "Select a league to bet in.",
                _ => "Could not place bet.",
            };
            return BadRequest(new { error });
        }

        var dto = await LoadBetDtoAsync(result.Bet!.Id, ct);
        return Created(string.Empty, dto! with { BoostApplied = result.AppliedBoostLabel });
    }

    [HttpGet("me")]
    public async Task<ActionResult<List<BetDto>>> GetMyBets(CancellationToken ct)
    {
        var bets = await db.Bets
            .Include(b => b.League)
            .Include(b => b.Legs).ThenInclude(l => l.Match)
            .Include(b => b.Legs).ThenInclude(l => l.Picks)
            .Where(b => b.UserId == CurrentUserId)
            .OrderByDescending(b => b.PlacedAt)
            .Take(20)
            .ToListAsync(ct);

        return Ok(bets.Select(ToBetDto).ToList());
    }

    private async Task<BetDto?> LoadBetDtoAsync(Guid betId, CancellationToken ct)
    {
        var bet = await db.Bets
            .Include(b => b.League)
            .Include(b => b.Legs).ThenInclude(l => l.Match)
            .Include(b => b.Legs).ThenInclude(l => l.Picks)
            .FirstOrDefaultAsync(b => b.Id == betId, ct);

        return bet is null ? null : ToBetDto(bet);
    }

    private static BetDto ToBetDto(Bet b) => new(
        b.Id, b.Stake, b.CombinedOdds, b.PotentialReturn, b.Status.ToString(), b.PlacedAt, b.SettledAt,
        b.LeagueId, b.LeagueId != null ? b.League!.Name : null, null,
        b.Legs.Select(l => new BetLegDto(
            l.MatchId,
            l.Match!.HomeTeam,
            l.Match!.AwayTeam,
            l.Match!.HomeTeamLogoUrl,
            l.Match!.AwayTeamLogoUrl,
            l.Match!.KickoffTime,
            l.OddsAtPlacement,
            l.Outcome.ToString(),
            l.Picks.Select(p => new BetLegPickDto(
                p.MarketType.ToString(), p.Line, p.Side.HasValue ? p.Side.ToString() : null,
                p.PredictedHomeScore, p.PredictedAwayScore, p.OddsAtPlacement, p.Outcome.ToString())).ToList()
        )).ToList());

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
