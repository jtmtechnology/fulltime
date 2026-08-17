using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Betting;

// Runs on its own timer (SettlementSweepService), separate from MatchSyncBackgroundService — the
// odds/score sync and bet settlement are independent concerns with independent cadences.
public class SettlementService(AppDbContext db, ILogger<SettlementService> logger)
{
    public async Task SweepAsync(CancellationToken ct = default)
    {
        await DeriveMatchResultsAsync(ct);
        await ResolveSelectionsAsync(ct);
        await SettleBetsAsync(ct);
    }

    private async Task DeriveMatchResultsAsync(CancellationToken ct)
    {
        var newlyFinished = await db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.Result == null
                && m.HomeScore != null && m.AwayScore != null)
            .ToListAsync(ct);

        if (newlyFinished.Count == 0)
        {
            return;
        }

        foreach (var match in newlyFinished)
        {
            match.Result = match.HomeScore == match.AwayScore
                ? MatchOutcome.Draw
                : match.HomeScore > match.AwayScore ? MatchOutcome.Home : MatchOutcome.Away;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Derived results for {Count} newly finished match(es)", newlyFinished.Count);
    }

    private async Task ResolveSelectionsAsync(CancellationToken ct)
    {
        var pendingSelections = await db.BetSelections
            .Include(s => s.Match)
            .Where(s => s.Outcome == SelectionOutcome.Pending && s.Match!.Result != null)
            .ToListAsync(ct);

        if (pendingSelections.Count == 0)
        {
            return;
        }

        foreach (var selection in pendingSelections)
        {
            selection.Outcome = selection.Pick == selection.Match!.Result
                ? SelectionOutcome.Correct
                : SelectionOutcome.Incorrect;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Resolved {Count} bet selection(s)", pendingSelections.Count);
    }

    private async Task SettleBetsAsync(CancellationToken ct)
    {
        var pendingBets = await db.Bets
            .Include(b => b.Selections)
            .Include(b => b.User)
            .Where(b => b.Status == BetStatus.Pending)
            .ToListAsync(ct);

        var settledCount = 0;
        var wonBets = new List<Bet>();

        foreach (var bet in pendingBets)
        {
            if (bet.Selections.Any(s => s.Outcome == SelectionOutcome.Incorrect))
            {
                bet.Status = BetStatus.Lost;
                bet.SettledAt = DateTime.UtcNow;
                settledCount++;
            }
            else if (bet.Selections.All(s => s.Outcome == SelectionOutcome.Correct))
            {
                bet.Status = BetStatus.Won;
                bet.SettledAt = DateTime.UtcNow;
                wonBets.Add(bet);
                settledCount++;
            }
            // else: at least one selection still Pending — an unfinished leg, leave the bet Pending.
        }

        if (settledCount == 0)
        {
            return;
        }

        // A won bet credits whichever pool its stake originally came from — the global Worldwide
        // balance for LeagueId == null, or that specific league's own membership balance otherwise.
        // Each pool is fully independent, so this must never touch User.Balance for a league bet.
        var wonLeagueBets = wonBets.Where(b => b.LeagueId is not null).ToList();
        var leagueIds = wonLeagueBets.Select(b => b.LeagueId!.Value).Distinct().ToList();
        var memberships = leagueIds.Count == 0
            ? []
            : await db.LeagueMemberships.Where(m => leagueIds.Contains(m.LeagueId)).ToListAsync(ct);
        var membershipLookup = memberships.ToDictionary(m => (m.LeagueId, m.UserId));

        foreach (var bet in wonLeagueBets)
        {
            if (membershipLookup.TryGetValue((bet.LeagueId!.Value, bet.UserId), out var membership))
            {
                membership.Balance += bet.PotentialReturn;
            }
            else
            {
                // Shouldn't happen — LeaguesController.LeaveLeague blocks leaving while a bet in
                // that league is still Pending. Log and move on rather than aborting the whole
                // sweep over one bad row.
                logger.LogError(
                    "Won bet {BetId} references league {LeagueId} but user {UserId} has no membership — winnings not credited",
                    bet.Id, bet.LeagueId, bet.UserId);
            }
        }

        foreach (var bet in wonBets.Where(b => b.LeagueId is null))
        {
            bet.User!.Balance += bet.PotentialReturn;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Settled {Count} bet(s)", settledCount);
    }
}
