using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Betting;

public record BetBuilderBoostStatus(bool Available, Guid? MatchId, string? HomeTeam, string? AwayTeam, decimal Percent);

// Picks one match a day - shared by every user, not randomized per-user (a single "match of the
// day" boost, the same way bet365's own Bet Builder Boost banner works) - eligible from the
// domestic English pyramid's top four tiers (Premier League/Championship/League One/League Two,
// see LeagueCatalog.AlwaysVisible for the same four IDs client-side) that's still Upcoming and
// already has Bet Builder odds priced. Never re-picked once chosen for the day, even if asked
// again later - see BetBuilderBoost's own comment.
public class BetBuilderBoostService(AppDbContext db, IOptions<BettingOptions> options)
{
    // Highlightly league IDs for Premier League/Championship/League One/League Two - same values
    // as FullTime.App.Shared's LeagueCatalog.AlwaysVisible[..4] (that RCL can't be referenced from
    // the API project, so this is the server-side mirror, same situation as HighlightlyLeagueMap.cs
    // vs LeagueCatalog.cs).
    private static readonly long[] EligibleLeagueIds = [33973, 34824, 35675, 36526];

    public async Task<BetBuilderBoostStatus> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var match = await GetOrPickTodaysMatchAsync(ct);
        if (match is null)
        {
            return new BetBuilderBoostStatus(false, null, null, null, options.Value.BetBuilderBoostPercent);
        }

        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("Current user not found.");
        var alreadyUsedToday = user.LastBetBuilderBoostDate == DateOnly.FromDateTime(DateTime.Now);

        return new BetBuilderBoostStatus(
            !alreadyUsedToday, match.Id, match.HomeTeam, match.AwayTeam, options.Value.BetBuilderBoostPercent);
    }

    // Called from BetService at placement time - re-reads today's featured match itself rather than
    // trusting anything the client sent, same "don't trust the slip" reasoning BetService already
    // applies to odds/match status. Silently returns NotApplied (never rejects the bet outright) if
    // the criteria aren't met - the client is expected to stop someone placing a sub-evens bet on
    // the featured match via the UI, but a bypassed/direct API call just places a normal, unboosted
    // bet rather than being refused.
    public async Task<(bool Applied, decimal Multiplier, string? Label)> TryConsumeBoostAsync(
        User user, Guid singleMatchId, decimal combinedOddsBeforeBoost, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (user.LastBetBuilderBoostDate == today)
        {
            return (false, 1m, null);
        }

        var todaysBoost = await db.BetBuilderBoosts.FirstOrDefaultAsync(b => b.Date == today, ct);
        if (todaysBoost is null || todaysBoost.MatchId != singleMatchId)
        {
            return (false, 1m, null);
        }

        if (combinedOddsBeforeBoost <= 2m)
        {
            return (false, 1m, null);
        }

        user.LastBetBuilderBoostDate = today;
        var percent = options.Value.BetBuilderBoostPercent;
        return (true, 1m + percent / 100m, $"Bet Builder Boost {percent:0.##}%");
    }

    private async Task<Match?> GetOrPickTodaysMatchAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var existing = await db.BetBuilderBoosts
            .Include(b => b.Match)
            .FirstOrDefaultAsync(b => b.Date == today, ct);
        if (existing is not null)
        {
            return existing.Match;
        }

        // Same UTC calendar-day window MatchesController.GetUpcoming uses for its own date-specific
        // query - "today's matches" means kicking off within today's UTC date, not merely Upcoming.
        var start = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var candidateIds = await db.Matches
            .Where(m => m.Status == MatchStatus.Upcoming && EligibleLeagueIds.Contains(m.LeagueId))
            .Where(m => m.KickoffTime >= start && m.KickoffTime < end)
            .Where(m => m.BetBuilderMarkets.Any())
            .Select(m => m.Id)
            .ToListAsync(ct);
        if (candidateIds.Count == 0)
        {
            return null;
        }

        var pickedId = candidateIds[Random.Shared.Next(candidateIds.Count)];
        db.BetBuilderBoosts.Add(new BetBuilderBoost { Id = Guid.NewGuid(), Date = today, MatchId = pickedId });
        await db.SaveChangesAsync(ct);

        return await db.Matches.FirstOrDefaultAsync(m => m.Id == pickedId, ct);
    }
}
