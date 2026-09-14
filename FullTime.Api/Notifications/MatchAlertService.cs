using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Notifications;

// The one place "who gets notified about this match transition" is decided - every detection hook
// (ApiFootballMatchSyncService for kickoff/half-time/full-time/goal, ApiFootballSettlementSupportService
// for red cards, MatchAlertLineupsCheckBackgroundService for lineups-out) just needs to know THAT a
// transition happened and calls NotifyAsync; this resolves scope + per-user preference + dedup.
public class MatchAlertService(AppDbContext db, PushNotificationService push, ILogger<MatchAlertService> logger)
{
    // Interested = favourited either team, favourited the league, or individually subscribed to
    // this exact match - a plain union (a user only needs to match one of the three), then narrowed
    // to whoever actually has this specific alert type turned on, then narrowed again to whoever
    // hasn't already been sent this exact (match, type) pair (SentMatchAlert's whole reason to
    // exist - a live-sync tick runs every few seconds, so without this a goal would otherwise fire
    // on every subsequent tick for the rest of the match).
    // sequence distinguishes repeat occurrences of the same alert type within one match (Goal,
    // RedCard) - always 0 for the four types that only ever happen once per match. See
    // SentMatchAlert.Sequence for what each call site should pass.
    public async Task NotifyAsync(Match match, MatchAlertType type, string title, string body, int sequence = 0, CancellationToken ct = default)
    {
        var scoped = db.Users
            .Where(u =>
                db.FavouriteTeams.Any(f => f.UserId == u.Id && (f.TeamId == match.HomeTeamId || f.TeamId == match.AwayTeamId))
                || db.FavouriteLeagues.Any(f => f.UserId == u.Id && f.LeagueId == match.LeagueId)
                || db.MatchAlertSubscriptions.Any(s => s.UserId == u.Id && s.MatchId == match.Id))
            .Select(u => u.Id);

        // EF Core can't translate a dynamic/reflected property lookup (e.g. EF.Property with a
        // variable name) into SQL - same class of limitation as the arbitrary-method-in-Where issue
        // this codebase already worked around elsewhere (see HighlightlyMatchSyncService.DeriveStatus's
        // .Contains() fix) - so which UserAlertPreferences column to check is picked with a plain
        // switch over static lambdas rather than one dynamic predicate.
        var withPreference = type switch
        {
            MatchAlertType.LineupsOut => scoped.Where(id => db.UserAlertPreferences.Any(p => p.UserId == id && p.LineupsOut)),
            MatchAlertType.Kickoff => scoped.Where(id => db.UserAlertPreferences.Any(p => p.UserId == id && p.Kickoff)),
            MatchAlertType.HalfTime => scoped.Where(id => db.UserAlertPreferences.Any(p => p.UserId == id && p.HalfTime)),
            MatchAlertType.Goal => scoped.Where(id => db.UserAlertPreferences.Any(p => p.UserId == id && p.Goal)),
            MatchAlertType.RedCard => scoped.Where(id => db.UserAlertPreferences.Any(p => p.UserId == id && p.RedCard)),
            MatchAlertType.FullTime => scoped.Where(id => db.UserAlertPreferences.Any(p => p.UserId == id && p.FullTime)),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        var interestedUserIds = await withPreference
            .Where(id => !db.SentMatchAlerts.Any(s =>
                s.UserId == id && s.MatchId == match.Id && s.AlertType == type && s.Sequence == sequence))
            .ToListAsync(ct);

        if (interestedUserIds.Count == 0)
        {
            return;
        }

        await push.SendToUsersAsync(interestedUserIds, title, body, ct);

        var sentAt = DateTime.UtcNow;
        db.SentMatchAlerts.AddRange(interestedUserIds.Select(userId => new SentMatchAlert
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MatchId = match.Id,
            AlertType = type,
            Sequence = sequence,
            SentAt = sentAt,
        }));
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Sent {AlertType} alert (seq {Sequence}) for match {MatchId} ({Home} v {Away}) to {Count} user(s)",
            type, sequence, match.Id, match.HomeTeam, match.AwayTeam, interestedUserIds.Count);
    }
}
