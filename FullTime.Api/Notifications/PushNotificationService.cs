using FirebaseAdmin.Messaging;
using FullTime.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Notifications;

public class PushNotificationService(AppDbContext db, ILogger<PushNotificationService> logger)
{
    public Task SendToUserAsync(Guid userId, string title, string body, CancellationToken ct = default) =>
        SendToUsersAsync([userId], title, body, ct);

    public async Task SendToUsersAsync(IEnumerable<Guid> userIds, string title, string body, CancellationToken ct = default)
    {
        var idList = userIds.ToList();
        if (idList.Count == 0) return;

        var tokens = await db.DeviceTokens.Where(d => idList.Contains(d.UserId)).ToListAsync(ct);
        if (tokens.Count == 0)
        {
            // Previously silent — meant a "why didn't I get my push?" report couldn't be
            // distinguished from a real send failure without this line, since nothing else here
            // logs anything on success either.
            logger.LogInformation(
                "Push \"{Title}\" not sent to {UserCount} user(s) — no registered device token(s)",
                title, idList.Count);
            return;
        }

        var messaging = FirebaseMessaging.DefaultInstance;
        var staleTokenIds = new List<Guid>();
        var sentCount = 0;

        foreach (var deviceToken in tokens)
        {
            try
            {
                // Message.Token is flagged obsolete in favor of Fid (Firebase Installation ID), but
                // client SDKs across platforms still hand back a registration token, not a raw FID —
                // there's nothing to migrate to yet without a matching client-side change.
#pragma warning disable CS0618
                var messageId = await messaging.SendAsync(new Message
                {
                    Token = deviceToken.Token,
                    Notification = new Notification { Title = title, Body = body },
                }, ct);
#pragma warning restore CS0618
                sentCount++;
                logger.LogInformation(
                    "Sent push \"{Title}\" to device {DeviceTokenId} (user {UserId}): {MessageId}",
                    title, deviceToken.Id, deviceToken.UserId, messageId);
            }
            catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                // The device uninstalled the app or the token otherwise expired — stop trying it.
                logger.LogInformation(
                    "Device token {DeviceTokenId} (user {UserId}) is stale ({ErrorCode}) — removing it",
                    deviceToken.Id, deviceToken.UserId, ex.MessagingErrorCode);
                staleTokenIds.Add(deviceToken.Id);
            }
            catch (Exception ex)
            {
                // A push failing must never break the caller's own operation (settlement, league join).
                logger.LogError(ex, "Failed to send push notification to device {DeviceTokenId}", deviceToken.Id);
            }
        }

        if (staleTokenIds.Count > 0)
        {
            await db.DeviceTokens.Where(d => staleTokenIds.Contains(d.Id)).ExecuteDeleteAsync(ct);
        }

        logger.LogInformation(
            "Push \"{Title}\": sent to {SentCount}/{TotalCount} device(s), {StaleCount} stale",
            title, sentCount, tokens.Count, staleTokenIds.Count);
    }
}
