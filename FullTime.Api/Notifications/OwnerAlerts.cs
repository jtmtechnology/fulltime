using FullTime.Api.Auth;
using FullTime.Api.BetBuilder.ApiFootball;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Notifications;

// Growth emails to the owner (new installs, new registrations). Reuses ApiFootball:AlertEmail
// rather than adding a new setting, since that's already the owner's ops inbox on the VM.
public class OwnerAlerts(IEmailSender emailSender, IOptions<ApiFootballOptions> options, ILogger<OwnerAlerts> logger)
{
    // api/installs is anonymous, so anyone could post random install IDs at it - this caps the damage
    // to the owner's inbox (and the Brevo daily quota) rather than trying to tell real installs apart.
    private const int MaxInstallAlertsPerDay = 50;

    // Same reasoning (api/client-errors is anonymous too), and one broken screen hit by every user
    // would otherwise send an email per user per attempt. The journal still gets every report.
    private const int MaxClientErrorAlertsPerDay = 20;

    private readonly Lock _gate = new();
    private DateOnly _installAlertDay;
    private int _installAlertsToday;
    private DateOnly _clientErrorAlertDay;
    private int _clientErrorAlertsToday;

    public void NewRegistration(string name, string email) =>
        Send("FullTime: new registration", $"{name} <{email}> just registered.\n\nTime: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

    public void NewLeague(string leagueName, string creatorName, string creatorEmail) =>
        Send("FullTime: new league created",
            $"{creatorName} <{creatorEmail}> just created the league \"{leagueName}\".\n\nTime: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

    public void NewInstall(string platform, string? appVersion)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (_installAlertDay != today)
            {
                _installAlertDay = today;
                _installAlertsToday = 0;
            }

            if (++_installAlertsToday > MaxInstallAlertsPerDay)
            {
                logger.LogWarning("Install alert cap ({Cap}/day) reached - not emailing", MaxInstallAlertsPerDay);
                return;
            }
        }

        Send("FullTime: new user opened the app",
            $"Someone opened the app for the first time without being logged in.\n\n" +
            $"Platform: {platform}\nApp version: {appVersion ?? "unknown"}\nTime: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
    }

    public void ClientError(string page, string userId, string device, string appVersion, string exception)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (_clientErrorAlertDay != today)
            {
                _clientErrorAlertDay = today;
                _clientErrorAlertsToday = 0;
            }

            if (++_clientErrorAlertsToday > MaxClientErrorAlertsPerDay)
            {
                logger.LogWarning("Client error alert cap ({Cap}/day) reached - not emailing", MaxClientErrorAlertsPerDay);
                return;
            }
        }

        Send("FullTime: app showed \"Something went wrong\"",
            $"Page: {page}\nUser: {userId}\nDevice: {device}\nApp version: {appVersion}\n" +
            $"Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n\n{exception}");
    }

    // Fire-and-forget, same as the match-sync alerts - a slow or failing SMTP relay must never hold
    // up or fail the request that triggered it.
    private void Send(string subject, string body)
    {
        var toEmail = options.Value.AlertEmail;
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await emailSender.SendAsync(toEmail, subject, body);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send owner alert '{Subject}'", subject);
            }
        });
    }
}
