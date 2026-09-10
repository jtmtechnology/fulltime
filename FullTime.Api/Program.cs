using System.Text;
using FirebaseAdmin;
using FullTime.Api.Auth;
using FullTime.Api.BetBuilder;
using FullTime.Api.BetBuilder.ApiFootball;
using FullTime.Api.BetBuilder.OddsApi;
using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Leagues;
using FullTime.Api.Notifications;
using FullTime.Api.Realtime;
using FullTime.Api.Spin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

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
// Rollback-by-config toggle (see ProvidersOptions) — both Highlightly and its API-Football/
// the-odds-api replacements stay compiled in and DI-registered regardless of which is active; only
// the *hosted services* (the background loops that actually call out) are conditional, read
// straight from configuration since hosted-service registration happens before DI is built.
var providers = builder.Configuration.GetSection(ProvidersOptions.SectionName).Get<ProvidersOptions>()
    ?? new ProvidersOptions();
builder.Services.Configure<ProvidersOptions>(builder.Configuration.GetSection(ProvidersOptions.SectionName));

builder.Services.Configure<HighlightlyOptions>(builder.Configuration.GetSection(HighlightlyOptions.SectionName));
builder.Services.AddHttpClient<HighlightlyClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HighlightlyOptions>>().Value;
    client.BaseAddress = new Uri($"https://{opts.ApiHost}/");
    client.DefaultRequestHeaders.Add("x-rapidapi-host", opts.ApiHost);
    client.DefaultRequestHeaders.Add("x-rapidapi-key", opts.ApiKey);
});
builder.Services.AddScoped<HighlightlyMatchSyncService>();
builder.Services.AddScoped<BetBuilderSyncService>();

builder.Services.Configure<ApiFootballOptions>(builder.Configuration.GetSection(ApiFootballOptions.SectionName));
builder.Services.AddHttpClient<ApiFootballClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiFootballOptions>>().Value;
    client.BaseAddress = new Uri($"https://{opts.ApiHost}/");
    client.DefaultRequestHeaders.Add("x-apisports-key", opts.ApiKey);
});
builder.Services.AddScoped<ApiFootballMatchSyncService>();
builder.Services.AddScoped<ApiFootballSettlementSupportService>();

builder.Services.Configure<OddsApiOptions>(builder.Configuration.GetSection(OddsApiOptions.SectionName));
// The-odds-api's key is a query-string param per request, not a header (unlike Highlightly/
// API-Football) — no default-header setup needed here, OddsApiClient appends it itself.
builder.Services.AddHttpClient<OddsApiClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OddsApiOptions>>().Value;
    client.BaseAddress = new Uri($"https://{opts.ApiHost}/");
});
builder.Services.AddScoped<OddsApiMarketService>();
builder.Services.AddScoped<PlayerPropsService>();

if (providers.LiveScoreSource == "ApiFootball")
{
    builder.Services.AddHostedService<ApiFootballMatchSyncBackgroundService>();
    builder.Services.AddHostedService<ApiFootballFixtureDiscoveryBackgroundService>();
    builder.Services.AddHostedService<ApiFootballSettlementSupportBackgroundService>();
}
else
{
    builder.Services.AddHostedService<HighlightlyMatchSyncBackgroundService>();
    builder.Services.AddHostedService<HighlightlyFixtureDiscoveryBackgroundService>();

    // As of 2026-09-07, GoalScorerResolutionBackgroundService's ResolveMatchEventsAsync settles
    // PlayerPropsService's EPL player-prop bets (goalscorer/cards/red cards/assists) too, not just
    // FirstTeamToScore - Highlightly's events/statistics endpoints cover all of it except shots,
    // which Highlightly has no per-player data for at all. PlayerStatsSettlementBackgroundService
    // stays wired in specifically for PlayerShotsOnTarget/PlayerShots - API-Football is otherwise
    // scoped back to squad lookups only (see PlayerPropsService).
    builder.Services.AddHostedService<GoalScorerResolutionBackgroundService>();
    builder.Services.AddHostedService<PlayerStatsSettlementBackgroundService>();
}

// BetBuilderSyncBackgroundService is Highlightly's own timer-driven odds sync — only needed when
// Highlightly is still the markets source. the-odds-api's replacement (OddsApiMarketService) is
// called on-demand from MatchesController instead, never from a timer.
if (providers.MarketsSource != "OddsApi")
{
    builder.Services.AddHostedService<BetBuilderSyncBackgroundService>();
}

builder.Services.AddScoped<BetService>();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddHostedService<SettlementSweepService>();
builder.Services.AddScoped<WeeklyTopUpService>();
builder.Services.AddHostedService<WeeklyTopUpBackgroundService>();

builder.Services.AddScoped<LeagueService>();

builder.Services.AddScoped<SpinService>();
builder.Services.AddScoped<SpinReminderService>();
builder.Services.AddHostedService<SpinReminderBackgroundService>();

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
app.MapHub<MatchUpdatesHub>("/hubs/matches");

app.MapGet("/api/config", async (
    Microsoft.Extensions.Options.IOptions<ProvidersOptions> providersOptions,
    HighlightlyMatchSyncService highlightlySyncService,
    ApiFootballMatchSyncService apiFootballSyncService) =>
{
    var delay = providersOptions.Value.LiveScoreSource == "ApiFootball"
        ? await apiFootballSyncService.NextPollDelayAsync()
        : await highlightlySyncService.NextPollDelayAsync();
    return Results.Ok(new { refreshIntervalSeconds = (int)delay.TotalSeconds });
});

app.Run();
