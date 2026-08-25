using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

// Standalone from FullTime.Api on purpose - a marketing site shouldn't need the main API
// redeployed just to tweak copy, so it gets its own small SMTP sender (same MailKit approach as
// FullTime.Api's SmtpEmailSender) rather than calling into the API's /api endpoints.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

const string ContactRecipient = "fulltime@jtmtechnology.co.uk";
var smtpHost = builder.Configuration["Email:SmtpHost"] ?? "smtp-relay.brevo.com";
var smtpPort = int.Parse(builder.Configuration["Email:SmtpPort"] ?? "587");
var smtpUsername = builder.Configuration["Email:SmtpUsername"];
var smtpPassword = builder.Configuration["Email:SmtpPassword"];
var fromAddress = builder.Configuration["Email:FromAddress"] ?? ContactRecipient;

app.MapPost("/api/contact", async (ContactRequest request, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Name, email and message are all required." });
    }

    if (!request.Email.Contains('@'))
    {
        return Results.BadRequest(new { error = "That doesn't look like a valid email address." });
    }

    if (string.IsNullOrEmpty(smtpUsername) || string.IsNullOrEmpty(smtpPassword))
    {
        logger.LogError("Contact form submitted but SMTP credentials are not configured");
        return Results.Problem("Email isn't configured on the server yet.", statusCode: 500);
    }

    try
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("FullTime Website", fromAddress));
        message.To.Add(MailboxAddress.Parse(ContactRecipient));
        // Replying to the notification email goes straight back to whoever submitted the form,
        // not to the website's own From address.
        message.ReplyTo.Add(new MailboxAddress(request.Name, request.Email));
        message.Subject = $"FullTime contact form — {request.Name}";
        message.Body = new TextPart("plain") { Text = $"From: {request.Name} <{request.Email}>\n\n{request.Message}" };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(smtpUsername, smtpPassword);
        try
        {
            await client.SendAsync(message);
        }
        finally
        {
            await client.DisconnectAsync(true);
        }

        logger.LogInformation("Contact form email sent from {Email}", request.Email);
        return Results.Ok(new { message = "Thanks — we'll get back to you soon." });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send contact form email");
        return Results.Problem("Something went wrong sending your message — please try again.", statusCode: 500);
    }
});

app.Run();

record ContactRequest(string Name, string Email, string Message);
