using FullTime.App.Web.Components;
using FullTime.App.Shared.Services;
using FullTime.App.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the FullTime.App.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<IJwtStore, WebJwtStore>();
builder.Services.AddScoped<ILocaleProvider, WebLocaleProvider>();
builder.Services.AddScoped<ISlipStore, WebSlipStore>();
builder.Services.AddScoped<IActiveContextStore, WebActiveContextStore>();
builder.Services.AddScoped<IPushRegistrar, WebPushRegistrar>();
builder.Services.AddScoped<IAdsRemovalService, WebAdsRemovalService>();
builder.Services.AddScoped<IInterstitialAdService, WebInterstitialAdService>();
builder.Services.AddScoped<IMatchLeaguePreferenceStore, WebMatchLeaguePreferenceStore>();
builder.Services.AddScoped<ICelebratedWinStore, WebCelebratedWinStore>();
builder.Services.AddSingleton<IHapticFeedback, WebHapticFeedback>();
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<BetSlipState>();
builder.Services.AddScoped<ActiveContextState>();
builder.Services.AddScoped<MatchLeaguePreferences>();
builder.Services.AddScoped<MatchUpdatesClient>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Api:BaseUrl configuration is missing.");
builder.Services.AddHttpClient<ApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(FullTime.App.Shared._Imports).Assembly);

app.Run();
