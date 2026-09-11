using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Betting;

public record LegPickInput(
    MarketType MarketType, decimal? Line, SelectionSide? Side, int? PredictedHomeScore = null, int? PredictedAwayScore = null,
    string? PlayerName = null, string? Team = null);
public record LegInput(Guid MatchId, List<LegPickInput> Picks);

public class BetService(AppDbContext db, BetBuilderBoostService boostService, ILogger<BetService> logger)
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
                var market = await FindMarketAsync(
                    legInput.MatchId, pickInput.MarketType, pickInput.Line, pickInput.Side,
                    pickInput.PredictedHomeScore, pickInput.PredictedAwayScore, pickInput.PlayerName, pickInput.Team, ct);
                var odds = market is null
                    ? await GetMatchResultOddsAsync(legInput.MatchId, pickInput.MarketType, pickInput.Side, ct)
                    : market.Price;
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
                    // Copied from the matched market row, never trusted from raw client input — a
                    // client could otherwise claim any PlayerName/Team for a pick it didn't actually
                    // price.
                    PlayerName = market?.PlayerName,
                    Team = market?.Team,
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
        else if (betLegs.Count == 1)
        {
            // Bet Builder Boost (see BetBuilderBoostService) only ever applies to a same-game multi
            // confined entirely to today's featured match - takes a back seat to an already-won
            // Daily Spinner boost rather than stacking with it.
            var (applied, betBuilderMultiplier, label) = await boostService.TryConsumeBoostAsync(
                user, betLegs[0].MatchId, betLegs[0].Picks.Count, combinedOdds, ct);
            if (applied)
            {
                combinedOdds *= betBuilderMultiplier;
                appliedBoostLabel = label;
            }
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

    private async Task<decimal?> GetMatchResultOddsAsync(Guid matchId, MarketType marketType, SelectionSide? side, CancellationToken ct)
    {
        if (marketType != MarketType.MatchResult)
        {
            return null;
        }

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

    // BetBuilderMarket is one row per priced outcome, so this is a direct lookup by whichever key
    // fields the market type actually uses — Side for OverUnder/BothTeamsToScore/FirstTeamToScore/
    // TotalCorners, PredictedHomeScore/PredictedAwayScore for CorrectScore, and additionally
    // PlayerName for the four player-prop types (PlayerGoalscorerAnytime/PlayerCard/
    // PlayerShotsOnTarget/PlayerAssists) — without it, two different players both priced e.g.
    // "Over 0.5 shots on target" under the same MarketType/Line/Side would be an ambiguous lookup
    // (confirmed a real bug during the cutover plan's research; this is the fix). TeamCorners/
    // TeamCards (Phase 2, 2026-09-10) have the exact same ambiguity the other way round - "Home
    // Over 5.5" and "Away Over 5.5" share a MarketType/Line/Side, disambiguated by Team instead of
    // PlayerName.
    private async Task<BetBuilderMarket?> FindMarketAsync(
        Guid matchId, MarketType marketType, decimal? line, SelectionSide? side,
        int? predictedHomeScore, int? predictedAwayScore, string? playerName, string? team, CancellationToken ct)
    {
        if (marketType == MarketType.MatchResult)
        {
            return null;
        }

        var query = db.BetBuilderMarkets.Where(m => m.MatchId == matchId && m.MarketType == marketType && m.Line == line);
        query = marketType == MarketType.CorrectScore
            ? query.Where(m => m.PredictedHomeScore == predictedHomeScore && m.PredictedAwayScore == predictedAwayScore)
            : query.Where(m => m.Side == side);

        if (IsPlayerPropMarket(marketType))
        {
            query = query.Where(m => m.PlayerName == playerName);
        }

        if (IsTeamScopedMarket(marketType))
        {
            query = query.Where(m => m.Team == team);
        }

        return await query.OrderByDescending(m => m.FetchedAt).FirstOrDefaultAsync(ct);
    }

    private static bool IsPlayerPropMarket(MarketType marketType) => marketType is
        MarketType.PlayerGoalscorerAnytime or MarketType.PlayerCard or MarketType.PlayerShotsOnTarget
        or MarketType.PlayerAssists or MarketType.PlayerRedCard or MarketType.PlayerShots or MarketType.PlayerFoulsCommitted;

    private static bool IsTeamScopedMarket(MarketType marketType) => marketType is
        MarketType.TeamCorners or MarketType.TeamCards;
}
