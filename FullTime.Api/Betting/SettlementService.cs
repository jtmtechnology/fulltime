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

    // Resolves each individual market pick once its match has a final score — pure DB reads for
    // most market types, plus provider-dependent guards: FirstTeamToScore additionally needs
    // Match.FirstGoalScorerSide resolved; TotalCorners/goalscorer/card/red-card/assists additionally
    // need Match.EventsFinalizedAt set (BetBuilderSyncService.ResolveMatchEventsAsync, Highlightly's
    // events/statistics endpoints); the two shots markets additionally need
    // Match.PlayerStatsResolvedAt set (ApiFootballSettlementSupportService.ResolvePlayerStatsAsync) -
    // Highlightly has no per-player shots data at all (checked for real 2026-09-07), so those two
    // still settle off API-Football, kept wired in for exactly this on top of its squad-lookup use
    // in PlayerPropsService.
    // Static readonly collections (translated to SQL IN clauses via .Contains()), not a call to
    // RequiresPlayerStats(p.MarketType) inline in the Where() below — EF Core can't translate an
    // arbitrary C# method call into SQL (confirmed: this threw InvalidOperationException in
    // production the first time it ran, "Translation of method ... failed").
    private static readonly MarketType[] EventsDerivedMarketTypes =
    [
        MarketType.TotalCorners, MarketType.PlayerGoalscorerAnytime, MarketType.PlayerCard,
        MarketType.PlayerRedCard, MarketType.PlayerAssists,
    ];

    private static readonly MarketType[] PlayerStatMarketTypes =
    [
        MarketType.PlayerShotsOnTarget, MarketType.PlayerShots, MarketType.PlayerFoulsCommitted,
    ];

    // TeamCorners/TeamCards (Phase 2, 2026-09-10) read Match.HomeCorners/AwayCorners/HomeCards/
    // AwayCards, populated by the same ApiFootballSettlementSupportService.ResolvePlayerStatsAsync
    // fetch that already sets PlayerStatsResolvedAt for the shots markets above - not
    // EventsDerivedMarketTypes, since they don't depend on Match.Events at all.
    private static readonly MarketType[] TeamStatMarketTypes =
    [
        MarketType.TeamCorners, MarketType.TeamCards,
    ];

    private async Task ResolvePicksAsync(CancellationToken ct)
    {
        var pendingPicks = await db.BetLegPicks
            .Include(p => p.BetLeg)
            .ThenInclude(l => l!.Match)
            .ThenInclude(m => m!.Events)
            .Include(p => p.BetLeg)
            .ThenInclude(l => l!.Match)
            .ThenInclude(m => m!.PlayerStats)
            .Where(p => p.Outcome == SelectionOutcome.Pending && p.BetLeg!.Match!.Result != null
                && (p.MarketType != MarketType.FirstTeamToScore || p.BetLeg!.Match!.FirstGoalScorerSide != null)
                && (!EventsDerivedMarketTypes.Contains(p.MarketType) || p.BetLeg!.Match!.EventsFinalizedAt != null)
                && (!PlayerStatMarketTypes.Contains(p.MarketType) || p.BetLeg!.Match!.PlayerStatsResolvedAt != null)
                && (!TeamStatMarketTypes.Contains(p.MarketType) || p.BetLeg!.Match!.PlayerStatsResolvedAt != null))
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

        switch (pick.MarketType)
        {
            case MarketType.MatchResult:
                return pick.Side switch
                {
                    SelectionSide.Home => match.Result == MatchOutcome.Home,
                    SelectionSide.Draw => match.Result == MatchOutcome.Draw,
                    SelectionSide.Away => match.Result == MatchOutcome.Away,
                    _ => false,
                };
            case MarketType.OverUnder:
                return pick.Side switch
                {
                    SelectionSide.Over => home + away > pick.Line!.Value,
                    SelectionSide.Under => home + away < pick.Line!.Value,
                    _ => false,
                };
            case MarketType.BothTeamsToScore:
                return pick.Side switch
                {
                    SelectionSide.Yes => home > 0 && away > 0,
                    SelectionSide.No => !(home > 0 && away > 0),
                    _ => false,
                };
            case MarketType.CorrectScore:
                return home == pick.PredictedHomeScore && away == pick.PredictedAwayScore;
            case MarketType.FirstTeamToScore:
                return pick.Side == match.FirstGoalScorerSide;
            case MarketType.TotalCorners:
                var corners = match.TotalCorners ?? 0;
                return pick.Side switch
                {
                    SelectionSide.Over => corners > pick.Line!.Value,
                    SelectionSide.Under => corners < pick.Line!.Value,
                    _ => false,
                };
            case MarketType.PlayerGoalscorerAnytime:
            {
                var scored = FindPlayerEvent(match, pick, "Goal") is not null;
                return pick.Side switch { SelectionSide.Yes => scored, SelectionSide.No => !scored, _ => false };
            }
            case MarketType.PlayerCard:
            {
                var booked = FindPlayerEvent(match, pick, "Yellow Card") is not null;
                return pick.Side switch { SelectionSide.Yes => booked, SelectionSide.No => !booked, _ => false };
            }
            case MarketType.PlayerRedCard:
            {
                var sentOff = FindPlayerEvent(match, pick, "Red Card") is not null;
                return pick.Side switch { SelectionSide.Yes => sentOff, SelectionSide.No => !sentOff, _ => false };
            }
            case MarketType.PlayerAssists:
            {
                var assists = CountAssists(match, pick);
                return pick.Side switch
                {
                    SelectionSide.Over => assists > pick.Line!.Value,
                    SelectionSide.Under => assists < pick.Line!.Value,
                    _ => false,
                };
            }
            case MarketType.PlayerShotsOnTarget:
            {
                var shots = FindPlayerStat(match, pick)?.ShotsOnTarget ?? 0;
                return pick.Side switch
                {
                    SelectionSide.Over => shots > pick.Line!.Value,
                    SelectionSide.Under => shots < pick.Line!.Value,
                    _ => false,
                };
            }
            case MarketType.PlayerShots:
            {
                var shots = FindPlayerStat(match, pick)?.TotalShots ?? 0;
                return pick.Side switch
                {
                    SelectionSide.Over => shots > pick.Line!.Value,
                    SelectionSide.Under => shots < pick.Line!.Value,
                    _ => false,
                };
            }
            case MarketType.PlayerFoulsCommitted:
            {
                var fouls = FindPlayerStat(match, pick)?.FoulsCommitted ?? 0;
                return pick.Side switch
                {
                    SelectionSide.Over => fouls > pick.Line!.Value,
                    SelectionSide.Under => fouls < pick.Line!.Value,
                    _ => false,
                };
            }
            case MarketType.TeamCorners:
            {
                var teamCorners = (pick.Team == "Home" ? match.HomeCorners : match.AwayCorners) ?? 0;
                return pick.Side switch
                {
                    SelectionSide.Over => teamCorners > pick.Line!.Value,
                    SelectionSide.Under => teamCorners < pick.Line!.Value,
                    _ => false,
                };
            }
            case MarketType.TeamCards:
            {
                var cards = (pick.Team == "Home" ? match.HomeCards : match.AwayCards) ?? 0;
                return pick.Side switch
                {
                    SelectionSide.Over => cards > pick.Line!.Value,
                    SelectionSide.Under => cards < pick.Line!.Value,
                    _ => false,
                };
            }
            default:
                return false;
        }
    }

    // Matched by surname + team side, same reasoning as FindPlayerEvent below - API-Football's
    // player names ("B. Saka") and the-odds-api's ("Bukayo Saka") format differently.
    private static MatchPlayerStat? FindPlayerStat(Match match, BetLegPick pick)
    {
        var surname = Surname(pick.PlayerName);
        var team = pick.Team switch { "Home" => SelectionSide.Home, "Away" => SelectionSide.Away, _ => (SelectionSide?)null };
        return match.PlayerStats.FirstOrDefault(s =>
            string.Equals(Surname(s.PlayerName), surname, StringComparison.OrdinalIgnoreCase) && (team is null || s.Team == team));
    }

    // Matched by surname + team side, not exact PlayerName equality - the-odds-api (where
    // BetLegPick.PlayerName comes from, copied from BetBuilderMarket at placement time) and
    // Highlightly (where MatchEvent.PlayerName comes from) format player names differently
    // ("Bukayo Saka" vs "B. Saka", confirmed real 2026-09-07 - the same mismatch API-Football's
    // player names had), so an exact match would silently miss most players and settle every pick as
    // "didn't happen" rather than resolving correctly. Same surname heuristic PlayerPropsService
    // already uses for team assignment. Team side is included to disambiguate the rare case of two
    // same-surnamed players on opposite sides in one match.
    //
    // A player with no matching event genuinely didn't do the thing (no goal, no card) — that's a
    // legitimate No/0 outcome, not "still unresolved" (the ResolvePicksAsync guard above already
    // ensures EventsFinalizedAt is set before any pick here is evaluated at all).
    private static MatchEvent? FindPlayerEvent(Match match, BetLegPick pick, string eventType)
    {
        var surname = Surname(pick.PlayerName);
        var team = pick.Team switch { "Home" => SelectionSide.Home, "Away" => SelectionSide.Away, _ => (SelectionSide?)null };
        return match.Events.FirstOrDefault(e =>
            e.Type == eventType && string.Equals(Surname(e.PlayerName), surname, StringComparison.OrdinalIgnoreCase)
            && (team is null || e.Team == team));
    }

    // Highlightly has no standalone "assists" counter - an assist only exists attached to a Goal
    // event (AssistPlayerName), so counting them means counting Goal events this player assisted,
    // not looking up a single stat. The assist provider is always on the same side as the goal
    // itself, so e.Team (the scorer's team) is also the assister's team - no separate team field
    // needed on the assist side.
    private static int CountAssists(Match match, BetLegPick pick)
    {
        var surname = Surname(pick.PlayerName);
        var team = pick.Team switch { "Home" => SelectionSide.Home, "Away" => SelectionSide.Away, _ => (SelectionSide?)null };
        return match.Events.Count(e =>
            e.Type == "Goal" && string.Equals(Surname(e.AssistPlayerName), surname, StringComparison.OrdinalIgnoreCase)
            && (team is null || e.Team == team));
    }

    // "J. Pickford" -> "Pickford", "Jordan Pickford" -> "Pickford" - same heuristic as
    // OddsApiMarketService.Surname/PlayerPropsService.Surname (the common ground between
    // Highlightly's abbreviated names and the-odds-api's full ones).
    private static string Surname(string? name) =>
        name?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[^1] : name ?? "";

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
