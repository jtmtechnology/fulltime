using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Betting;

public record LegPickInput(
    MarketType MarketType, decimal? Line, SelectionSide? Side, int? PredictedHomeScore = null, int? PredictedAwayScore = null);
public record LegInput(Guid MatchId, List<LegPickInput> Picks);

public class BetService(AppDbContext db, ILogger<BetService> logger)
{
    public async Task<PlaceBetResult> PlaceBetAsync(
        Guid userId, decimal stake, List<LegInput> legs, Guid? leagueId,
        CancellationToken ct = default)
    {
        if (legs.Count == 0 || legs.Any(l => l.Picks.Count == 0))
        {
            return new PlaceBetResult(PlaceBetOutcome.NoSelections);
        }

        if (legs.Select(l => l.MatchId).Distinct().Count() != legs.Count)
        {
            return new PlaceBetResult(PlaceBetOutcome.DuplicateMatch);
        }

        if (stake <= 0)
        {
            return new PlaceBetResult(PlaceBetOutcome.InvalidStake);
        }

        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return new PlaceBetResult(PlaceBetOutcome.InsufficientBalance);
        }

        // Every bet now has to be placed "in" a specific league — there's no standalone Worldwide
        // pool any more (the Worldwide leaderboard is a computed average across a user's leagues,
        // not its own bankroll; see LeaderboardController). Old bets with LeagueId == null from
        // before this change are left as historical records — only new placements are blocked here.
        if (leagueId is null)
        {
            return new PlaceBetResult(PlaceBetOutcome.NoLeagueSelected);
        }

        var membership = await db.LeagueMemberships
            .FirstOrDefaultAsync(m => m.LeagueId == leagueId.Value && m.UserId == userId, ct);
        if (membership is null)
        {
            return new PlaceBetResult(PlaceBetOutcome.InvalidLeague);
        }

        if (stake > membership.Balance)
        {
            return new PlaceBetResult(PlaceBetOutcome.InsufficientBalance);
        }

        // Re-read each match's current status/odds server-side rather than trusting whatever the
        // client's slip displayed — protects against a stale price or a match that kicked off
        // while the bet was being built.
        var betId = Guid.NewGuid();
        var betLegs = new List<BetLeg>();
        decimal combinedOdds = 1;

        foreach (var legInput in legs)
        {
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == legInput.MatchId, ct);
            if (match is null || match.Status != MatchStatus.Upcoming)
            {
                return new PlaceBetResult(PlaceBetOutcome.MatchNotAvailable);
            }

            var legPicks = new List<BetLegPick>();
            decimal legOdds = 1;

            foreach (var pickInput in legInput.Picks)
            {
                var odds = await GetCurrentOddsAsync(
                    legInput.MatchId, pickInput.MarketType, pickInput.Line, pickInput.Side,
                    pickInput.PredictedHomeScore, pickInput.PredictedAwayScore, ct);
                if (odds is null)
                {
                    return new PlaceBetResult(PlaceBetOutcome.MatchNotAvailable);
                }

                legOdds *= odds.Value;
                legPicks.Add(new BetLegPick
                {
                    Id = Guid.NewGuid(),
                    MarketType = pickInput.MarketType,
                    Line = pickInput.Line,
                    Side = pickInput.Side,
                    PredictedHomeScore = pickInput.PredictedHomeScore,
                    PredictedAwayScore = pickInput.PredictedAwayScore,
                    OddsAtPlacement = odds.Value,
                    Outcome = SelectionOutcome.Pending,
                });
            }

            combinedOdds *= legOdds;
            betLegs.Add(new BetLeg
            {
                Id = Guid.NewGuid(),
                BetId = betId,
                MatchId = legInput.MatchId,
                OddsAtPlacement = legOdds,
                Outcome = SelectionOutcome.Pending,
                Picks = legPicks,
            });
        }

        // A pending Daily Spinner boost (see SpinService) applies to this bet's odds and is
        // consumed immediately - it's a one-shot "next bet" prize, never stacked or reused.
        string? appliedBoostLabel = null;
        if (user.PendingBoostMultiplier is { } boostMultiplier)
        {
            combinedOdds *= boostMultiplier;
            appliedBoostLabel = user.PendingBoostLabel;
            user.PendingBoostMultiplier = null;
            user.PendingBoostLabel = null;
        }

        var bet = new Bet
        {
            Id = betId,
            UserId = userId,
            LeagueId = leagueId,
            Stake = stake,
            CombinedOdds = combinedOdds,
            PotentialReturn = stake * combinedOdds,
            Status = BetStatus.Pending,
            PlacedAt = DateTime.UtcNow,
            Legs = betLegs,
            BoostApplied = appliedBoostLabel,
        };

        membership.Balance -= stake;

        db.Bets.Add(bet);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} placed a {LegCount}-leg bet {BetId} for {Stake} at combined odds {CombinedOdds}",
            userId, betLegs.Count, bet.Id, stake, combinedOdds);

        return new PlaceBetResult(PlaceBetOutcome.Success, bet);
    }

    private async Task<decimal?> GetCurrentOddsAsync(
        Guid matchId, MarketType marketType, decimal? line, SelectionSide? side,
        int? predictedHomeScore, int? predictedAwayScore, CancellationToken ct)
    {
        if (marketType == MarketType.MatchResult)
        {
            var latestOdds = await db.OddsSnapshots
                .Where(o => o.MatchId == matchId)
                .OrderByDescending(o => o.FetchedAt)
                .FirstOrDefaultAsync(ct);
            if (latestOdds is null) return null;

            return side switch
            {
                SelectionSide.Home => latestOdds.HomeOdds,
                SelectionSide.Draw => latestOdds.DrawOdds,
                SelectionSide.Away => latestOdds.AwayOdds,
                _ => null,
            };
        }

        // BetBuilderMarket is one row per priced outcome, so this is a direct lookup by whichever
        // key fields the market type actually uses — Side for OverUnder/BothTeamsToScore/
        // FirstTeamToScore, PredictedHomeScore/PredictedAwayScore for CorrectScore.
        var query = db.BetBuilderMarkets.Where(m => m.MatchId == matchId && m.MarketType == marketType && m.Line == line);
        query = marketType == MarketType.CorrectScore
            ? query.Where(m => m.PredictedHomeScore == predictedHomeScore && m.PredictedAwayScore == predictedAwayScore)
            : query.Where(m => m.Side == side);

        var market = await query.OrderByDescending(m => m.FetchedAt).FirstOrDefaultAsync(ct);
        return market?.Price;
    }
}
