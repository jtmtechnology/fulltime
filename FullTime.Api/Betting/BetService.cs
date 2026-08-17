using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Betting;

public class BetService(AppDbContext db, ILogger<BetService> logger)
{
    public async Task<PlaceBetResult> PlaceBetAsync(
        Guid userId, decimal stake, List<(Guid MatchId, MatchOutcome Pick)> selections, Guid? leagueId,
        CancellationToken ct = default)
    {
        if (selections.Count == 0)
        {
            return new PlaceBetResult(PlaceBetOutcome.NoSelections);
        }

        if (selections.Select(s => s.MatchId).Distinct().Count() != selections.Count)
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

        // Betting "in" a league debits/credits that league's own membership balance rather than
        // the user's global Worldwide balance — each pool is fully independent.
        LeagueMembership? membership = null;
        if (leagueId is { } leagueIdValue)
        {
            membership = await db.LeagueMemberships
                .FirstOrDefaultAsync(m => m.LeagueId == leagueIdValue && m.UserId == userId, ct);
            if (membership is null)
            {
                return new PlaceBetResult(PlaceBetOutcome.InvalidLeague);
            }
        }

        var availableBalance = membership?.Balance ?? user.Balance;
        if (stake > availableBalance)
        {
            return new PlaceBetResult(PlaceBetOutcome.InsufficientBalance);
        }

        // Re-read each match's current status/odds server-side rather than trusting whatever the
        // client's slip displayed — protects against a stale price or a match that kicked off
        // while the bet was being built.
        var betId = Guid.NewGuid();
        var betSelections = new List<BetSelection>();
        decimal combinedOdds = 1;

        foreach (var (matchId, pick) in selections)
        {
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (match is null || match.Status != MatchStatus.Upcoming)
            {
                return new PlaceBetResult(PlaceBetOutcome.MatchNotAvailable);
            }

            var latestOdds = await db.OddsSnapshots
                .Where(o => o.MatchId == matchId)
                .OrderByDescending(o => o.FetchedAt)
                .FirstOrDefaultAsync(ct);
            if (latestOdds is null)
            {
                return new PlaceBetResult(PlaceBetOutcome.MatchNotAvailable);
            }

            var odds = pick switch
            {
                MatchOutcome.Home => latestOdds.HomeOdds,
                MatchOutcome.Draw => latestOdds.DrawOdds,
                MatchOutcome.Away => latestOdds.AwayOdds,
                _ => throw new ArgumentOutOfRangeException(nameof(pick)),
            };

            combinedOdds *= odds;
            betSelections.Add(new BetSelection
            {
                Id = Guid.NewGuid(),
                BetId = betId,
                MatchId = matchId,
                Pick = pick,
                OddsAtPlacement = odds,
                Outcome = SelectionOutcome.Pending,
            });
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
            Selections = betSelections,
        };

        if (membership is not null)
        {
            membership.Balance -= stake;
        }
        else
        {
            user.Balance -= stake;
        }

        db.Bets.Add(bet);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} placed a {LegCount}-leg bet {BetId} for {Stake} at combined odds {CombinedOdds}",
            userId, betSelections.Count, bet.Id, stake, combinedOdds);

        return new PlaceBetResult(PlaceBetOutcome.Success, bet);
    }
}
