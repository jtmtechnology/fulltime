using FullTime.Api.Data;
using FullTime.Api.Localization;
using FullTime.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Betting;

// Runs on its own timer (WeeklyTopUpBackgroundService), independent of the settlement sweep -
// applying a flat top-up to every league membership has nothing to do with resolving bets.
public class WeeklyTopUpService(
    AppDbContext db,
    PushNotificationService push,
    IOptions<BettingOptions> options,
    ILogger<WeeklyTopUpService> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysSinceSunday = ((int)today.DayOfWeek - (int)DayOfWeek.Sunday + 7) % 7;
        var mostRecentSunday = today.AddDays(-daysSinceSunday);
        var amount = options.Value.WeeklyTopUpAmount;

        var due = await db.LeagueMemberships
            .Include(m => m.League)
            .Include(m => m.User)
            .Where(m => m.LastTopUpDate == null || m.LastTopUpDate < mostRecentSunday)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var membership in due)
        {
            // Both sides move together so Profit (Balance - StartingBalance) is unaffected by the
            // top-up - it's free money, not a betting result.
            membership.Balance += amount;
            membership.StartingBalance += amount;
            membership.LastTopUpDate = mostRecentSunday;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Weekly top-up: applied {Amount} to {Count} membership(s) for the week of {Sunday}",
            amount, due.Count, mostRecentSunday);

        // One push per membership (per user per league) - someone in three leagues gets three
        // separate notifications, each naming its own league, rather than one lumped-together message.
        foreach (var membership in due)
        {
            var symbol = CurrencyCatalog.SymbolFor(membership.User?.Country);
            await push.SendToUserAsync(
                membership.UserId,
                "Weekly top-up!",
                $"+{symbol}{amount:0.00} has been added to your balance in {membership.League?.Name ?? "your league"}.",
                ct);
        }
    }
}
