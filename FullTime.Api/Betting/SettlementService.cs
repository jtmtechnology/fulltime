using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Betting;

// Runs on its own timer (SettlementSweepService), separate from HighlightlyMatchSyncBackgroundService — the
// odds/score sync and bet settlement are independent concerns with independent cadences.
public class SettlementService(AppDbContext db, PushNotificationService push, ILogger<SettlementService> logger)
{
    public async Task SweepAsync(CancellationToken ct = default)
    {
        await DeriveMatchResultsAsync(ct);
        await ResolvePicksAsync(ct);
        await ResolveLegsAsync(ct);
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

    // Resolves each individual market pick (MatchResult, OverUnder, BothTeamsToScore, CorrectScore,
    // or FirstTeamToScore) once its match has a final score — pure DB reads for every market type
    // except FirstTeamToScore, which additionally needs Match.FirstGoalScorerSide to have been
    // resolved by BetBuilderSyncService first (see the extra guard below).
    private async Task ResolvePicksAsync(CancellationToken ct)
    {
        var pendingPicks = await db.BetLegPicks
            .Include(p => p.BetLeg)
            .ThenInclude(l => l!.Match)
            .Where(p => p.Outcome == SelectionOutcome.Pending && p.BetLeg!.Match!.Result != null
                && (p.MarketType != MarketType.FirstTeamToScore || p.BetLeg!.Match!.FirstGoalScorerSide != null))
            .ToListAsync(ct);

        if (pendingPicks.Count == 0)
        {
            return;
        }

        foreach (var pick in pendingPicks)
        {
            var match = pick.BetLeg!.Match!;
            pick.Outcome = IsPickCorrect(pick, match) ? SelectionOutcome.Correct : SelectionOutcome.Incorrect;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Resolved {Count} bet pick(s)", pendingPicks.Count);
    }

    private static bool IsPickCorrect(BetLegPick pick, Match match)
    {
        var home = match.HomeScore!.Value;
        var away = match.AwayScore!.Value;

        return pick.MarketType switch
        {
            MarketType.MatchResult => pick.Side switch
            {
                SelectionSide.Home => match.Result == MatchOutcome.Home,
                SelectionSide.Draw => match.Result == MatchOutcome.Draw,
                SelectionSide.Away => match.Result == MatchOutcome.Away,
                _ => false,
            },
            MarketType.OverUnder => pick.Side switch
            {
                SelectionSide.Over => home + away > pick.Line!.Value,
                SelectionSide.Under => home + away < pick.Line!.Value,
                _ => false,
            },
            MarketType.BothTeamsToScore => pick.Side switch
            {
                SelectionSide.Yes => home > 0 && away > 0,
                SelectionSide.No => !(home > 0 && away > 0),
                _ => false,
            },
            MarketType.CorrectScore => home == pick.PredictedHomeScore && away == pick.PredictedAwayScore,
            MarketType.FirstTeamToScore => pick.Side == match.FirstGoalScorerSide,
            _ => false,
        };
    }

    private async Task ResolveLegsAsync(CancellationToken ct)
    {
        var pendingLegs = await db.BetLegs
            .Include(l => l.Picks)
            .Where(l => l.Outcome == SelectionOutcome.Pending)
            .ToListAsync(ct);

        if (pendingLegs.Count == 0)
        {
            return;
        }

        var resolvedCount = 0;

        foreach (var leg in pendingLegs)
        {
            if (leg.Picks.Any(p => p.Outcome == SelectionOutcome.Incorrect))
            {
                leg.Outcome = SelectionOutcome.Incorrect;
                resolvedCount++;
            }
            else if (leg.Picks.All(p => p.Outcome == SelectionOutcome.Correct))
            {
                leg.Outcome = SelectionOutcome.Correct;
                resolvedCount++;
            }
            // else: at least one pick still Pending (an unfinished match within a same-game
            // multi's picks can't happen since they share one match, but leave the guard for
            // safety) — leave the leg Pending.
        }

        if (resolvedCount == 0)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Resolved {Count} bet leg(s)", resolvedCount);
    }

    private async Task SettleBetsAsync(CancellationToken ct)
    {
        var pendingBets = await db.Bets
            .Include(b => b.Legs).ThenInclude(l => l.Match)
            .Include(b => b.User)
            .Where(b => b.Status == BetStatus.Pending)
            .ToListAsync(ct);

        var settledCount = 0;
        var wonBets = new List<Bet>();
        var lostBets = new List<Bet>();

        foreach (var bet in pendingBets)
        {
            if (bet.Legs.Any(l => l.Outcome == SelectionOutcome.Incorrect))
            {
                bet.Status = BetStatus.Lost;
                bet.SettledAt = DateTime.UtcNow;
                lostBets.Add(bet);
                settledCount++;
            }
            else if (bet.Legs.All(l => l.Outcome == SelectionOutcome.Correct))
            {
                bet.Status = BetStatus.Won;
                bet.SettledAt = DateTime.UtcNow;
                wonBets.Add(bet);
                settledCount++;
            }
            // else: at least one leg still Pending — an unfinished match, leave the bet Pending.
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

        foreach (var bet in wonBets)
        {
            var symbol = Localization.CurrencyCatalog.SymbolFor(bet.User?.Country);
            await push.SendToUserAsync(bet.UserId, "Bet Won", $"{DescribeBet(bet)} — +{symbol}{bet.PotentialReturn:0.00}", ct);
        }

        foreach (var bet in lostBets)
        {
            await push.SendToUserAsync(bet.UserId, "Bet Lost", $"{DescribeBet(bet)} didn't come in.", ct);
        }
    }

    // A single-match bet (a straight pick or a same-game Bet Builder multi) is identified by its
    // teams; an accumulator across several matches just gets its leg count rather than listing
    // every team, which could otherwise make the notification unreadably long.
    private static string DescribeBet(Bet bet) => bet.Legs.Count == 1
        ? $"{bet.Legs[0].Match!.HomeTeam} v {bet.Legs[0].Match!.AwayTeam}"
        : $"{bet.Legs.Count} leg acca";
}
