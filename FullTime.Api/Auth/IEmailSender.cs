namespace FullTime.Api.Auth;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);

    Task SendHtmlAsync(string toEmail, string subject, string htmlBody, string textFallback, CancellationToken ct = default);
}
