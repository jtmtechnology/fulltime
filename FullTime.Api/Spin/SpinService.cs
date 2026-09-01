using FullTime.Api.Betting;
using FullTime.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Spin;

// Segment order below must be kept in sync by hand with FullTime.App.Shared's
// Models/SpinSegment.cs (SpinSegments.All) - the API can't reference that Razor Class Library
// (wrong dependency direction, and it'd drag Blazor/MAUI deps into a plain Web API), so this is
// the authoritative server-side mirror used to actually pick the winning segment and resolve its
// prize. Same "kept in sync by hand" situation as FullTime.Website/wwwroot/styles.css's design
// tokens vs FullTime.App.Shared/wwwroot/app.css.
public enum SpinPrizeType
{
    MysteryCash,
    BetBoost25,
    TryAgainTomorrow,
    OddsBoost2x,
    BetBoost50,
}

public class SpinService(AppDbContext db, IOptions<BettingOptions> options)
{
    private static readonly SpinPrizeType[] SegmentPrizes =
    [
        SpinPrizeType.MysteryCash,
        SpinPrizeType.BetBoost25,
        SpinPrizeType.TryAgainTomorrow,
        SpinPrizeType.MysteryCash,
        SpinPrizeType.OddsBoost2x,
        SpinPrizeType.BetBoost50,
        SpinPrizeType.MysteryCash,
        SpinPrizeType.TryAgainTomorrow,
    ];

    public async Task<(bool CanSpin, int Streak, decimal? PendingBoostMultiplier, string? PendingBoostLabel)> GetStatusAsync(
        Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("Current user not found.");

        return (user.LastSpinDate != DateOnly.FromDateTime(DateTime.Now), user.SpinStreak,
            user.PendingBoostMultiplier, user.PendingBoostLabel);
    }

    public async Task<SpinResult> SpinAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("Current user not found.");

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (user.LastSpinDate == today)
        {
            return new SpinResult(SpinOutcome.AlreadySpunToday);
        }

        var winningIndex = Random.Shared.Next(SegmentPrizes.Length);
        var prize = SegmentPrizes[winningIndex];

        // Cycles rather than capping: increments daily, wraps back to 1 the day after hitting 7
        // instead of sitting at 7 forever.
        var newStreak = user.LastSpinDate == today.AddDays(-1) ? user.SpinStreak + 1 : 1;
        var streakBonusAwarded = newStreak >= 7;
        user.LastSpinDate = today;
        user.SpinStreak = streakBonusAwarded ? 0 : newStreak;

        decimal? mysteryAmount = null;
        decimal? streakBonusAmount = null;
        string? boostLabel = null;

        if (prize == SpinPrizeType.MysteryCash)
        {
            mysteryAmount = Random.Shared.Next(1, 11) * 5;
            await CreditAllMembershipsAsync(userId, mysteryAmount.Value, ct);
        }

        if (streakBonusAwarded)
        {
            streakBonusAmount = options.Value.SpinStreakBonusAmount;
            await CreditAllMembershipsAsync(userId, streakBonusAmount.Value, ct);
        }

        // A landed boost always replaces whatever was already pending and unused - only the most
        // recently won boost is ever honoured on the next bet, never stacked.
        (decimal Multiplier, string Label)? boost = prize switch
        {
            SpinPrizeType.BetBoost25 => (1.25m, "Bet Boost 25%"),
            SpinPrizeType.BetBoost50 => (1.5m, "Bet Boost 50%"),
            SpinPrizeType.OddsBoost2x => (2m, "2x Odds Boost"),
            _ => null,
        };
        if (boost is { } b)
        {
            user.PendingBoostMultiplier = b.Multiplier;
            user.PendingBoostLabel = b.Label;
            boostLabel = b.Label;
        }

        await db.SaveChangesAsync(ct);

        return new SpinResult(SpinOutcome.Success, winningIndex, user.SpinStreak, mysteryAmount, streakBonusAmount, boostLabel);
    }

    // TEMP for testing only - re-opens the daily gate by clearing LastSpinDate, same behaviour the
    // old client-only bypass had. Note this also means the very next spin's streak calc no longer
    // sees "yesterday", so it restarts the streak at 1 rather than continuing it - matches what the
    // original bypass did too, just now enforced server-side. Remove this method and the
    // "reset-for-testing" endpoint in SpinController once testing is done.
    public async Task ResetForTestingAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null)
        {
            user.LastSpinDate = null;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task CreditAllMembershipsAsync(Guid userId, decimal amount, CancellationToken ct)
    {
        var memberships = await db.LeagueMemberships.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var membership in memberships)
        {
            // Both sides move together, same rule as WeeklyTopUpService - a spin prize is free
            // money, not a betting result, so it must not skew Profit (Balance - StartingBalance).
            membership.Balance += amount;
            membership.StartingBalance += amount;
        }
    }
}
