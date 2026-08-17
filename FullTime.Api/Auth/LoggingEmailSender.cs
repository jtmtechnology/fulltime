namespace FullTime.Api.Auth;

// Stands in for a real email provider. Logs the message (visible in run.log) instead of sending it,
// so verification/reset links are read from the log during local/friends use. Swap for a real
// IEmailSender implementation later without touching any caller.
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("Email to {ToEmail} — {Subject}\n{Body}", toEmail, subject, body);
        return Task.CompletedTask;
    }
}
