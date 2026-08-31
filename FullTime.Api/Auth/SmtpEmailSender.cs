using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FullTime.Api.Auth;

public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default) =>
        await SendMessageAsync(toEmail, subject, new TextPart("plain") { Text = body }, ct);

    public async Task SendHtmlAsync(string toEmail, string subject, string htmlBody, string textFallback, CancellationToken ct = default)
    {
        var alternative = new MultipartAlternative
        {
            new TextPart("plain") { Text = textFallback },
            new TextPart("html") { Text = htmlBody },
        };
        await SendMessageAsync(toEmail, subject, alternative, ct);
    }

    private async Task SendMessageAsync(string toEmail, string subject, MimeEntity body, CancellationToken ct)
    {
        var opts = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(opts.FromName, opts.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = body;

        using var client = new SmtpClient();
        await client.ConnectAsync(opts.SmtpHost, opts.SmtpPort, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(opts.SmtpUsername, opts.SmtpPassword, ct);

        try
        {
            await client.SendAsync(message, ct);
        }
        finally
        {
            await client.DisconnectAsync(true, ct);
        }

        logger.LogInformation("Email sent to {ToEmail} — {Subject}", toEmail, subject);
    }
}
