using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Spin;

// Nudges anyone who hasn't spun the Daily Spinner yet today, once their own local clock passes
// BettingOptions.SpinReminderHour. Runs independently of SpinService's request-time logic, on its
// own timer (SpinReminderBackgroundService) - same "own timer, independent of anything else"
// pattern as WeeklyTopUpService.
public class SpinReminderService(AppDbContext db, PushNotificationService push, IOptions<BettingOptions> options)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var reminderHour = options.Value.SpinReminderHour;

        // UtcOffsetMinutes is only ever set by the MAUI app (the only host that does push - see
        // DevicesController.Register), so this naturally already excludes Web-only accounts that
        // have no device to push to anyway.
        var candidates = await db.Users
            .Where(u => u.UtcOffsetMinutes != null)
            .ToListAsync(ct);

        foreach (var user in candidates)
        {
            var localNow = DateTime.UtcNow.AddMinutes(user.UtcOffsetMinutes!.Value);
            var localToday = DateOnly.FromDateTime(localNow);

            if (localNow.Hour < reminderHour) continue;
            if (user.LastSpinDate == localToday) continue;
            if (user.LastSpinReminderDate == localToday) continue;

            user.LastSpinReminderDate = localToday;
            await push.SendToUserAsync(
                user.Id, "Don't miss today's spin!", "Your free Daily Spinner spin is still waiting — grab it before the day's up.", ct);
        }

        if (candidates.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
