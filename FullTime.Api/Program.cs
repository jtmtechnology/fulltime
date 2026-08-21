using System.Text;
using FirebaseAdmin;
using FullTime.Api.Auth;
using FullTime.Api.BetBuilder;
using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Leagues;
using FullTime.Api.Notifications;
using FullTime.Api.OddsApi;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddMemoryCache();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.Configure<BettingOptions>(builder.Configuration.GetSection(BettingOptions.SectionName));

builder.Services.AddScoped<AuthService>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<OddsApiOptions>(builder.Configuration.GetSection(OddsApiOptions.SectionName));
builder.Services.AddHttpClient<FootballDataClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OddsApiOptions>>().Value;
    client.BaseAddress = new Uri($"https://{opts.ApiHost}/");
    client.DefaultRequestHeaders.Add("x-rapidapi-host", opts.ApiHost);
    client.DefaultRequestHeaders.Add("x-rapidapi-key", opts.ApiKey);
});
builder.Services.AddScoped<MatchSyncService>();
builder.Services.AddHostedService<MatchSyncBackgroundService>();

builder.Services.Configure<HighlightlyOptions>(builder.Configuration.GetSection(HighlightlyOptions.SectionName));
builder.Services.AddHttpClient<HighlightlyClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HighlightlyOptions>>().Value;
    client.BaseAddress = new Uri($"https://{opts.ApiHost}/");
    client.DefaultRequestHeaders.Add("x-rapidapi-host", opts.ApiHost);
    client.DefaultRequestHeaders.Add("x-rapidapi-key", opts.ApiKey);
});
builder.Services.AddScoped<BetBuilderSyncService>();
builder.Services.AddHostedService<BetBuilderSyncBackgroundService>();

builder.Services.AddScoped<BetService>();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddHostedService<SettlementSweepService>();

builder.Services.AddScoped<LeagueService>();

builder.Services.Configure<PushOptions>(builder.Configuration.GetSection(PushOptions.SectionName));
FirebaseApp.Create(new AppOptions
{
    Credential = CredentialFactory.FromFile<ServiceAccountCredential>(
        builder.Configuration.GetSection(PushOptions.SectionName)[nameof(PushOptions.ServiceAccountPath)]
        ?? throw new InvalidOperationException("Push:ServiceAccountPath configuration is missing.")).ToGoogleCredential(),
});
builder.Services.AddScoped<PushNotificationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Deployed as plain HTTP behind a firewall-restricted port on a bare IP (no domain yet, so no
// Let's Encrypt/HTTPS) — a private, low-stakes friend app doesn't need TLS, and leaving this
// middleware in would just log a warning on every single request forever.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/config", async (MatchSyncService syncService, Microsoft.Extensions.Options.IOptions<OddsApiOptions> opts) =>
{
    var hasLiveMatch = await syncService.HasLiveMatchAsync();
    var interval = hasLiveMatch ? opts.Value.LiveRefreshIntervalSeconds : opts.Value.IdleRefreshIntervalSeconds;
    return Results.Ok(new { refreshIntervalSeconds = interval });
});

app.Run();
