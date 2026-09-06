using FullTime.Api.Sandbox.Data;
using FullTime.Api.Sandbox.Options;
using FullTime.Api.Sandbox.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<SandboxDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<ApiFootballOptions>(builder.Configuration.GetSection(ApiFootballOptions.SectionName));
builder.Services.AddHttpClient<ApiFootballClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiFootballOptions>>().Value;
    client.BaseAddress = new Uri($"https://{opts.ApiHost}/");
    client.DefaultRequestHeaders.Add("x-apisports-key", opts.ApiKey);
});

builder.Services.Configure<OddsApiOptions>(builder.Configuration.GetSection(OddsApiOptions.SectionName));
builder.Services.AddHttpClient<OddsApiClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OddsApiOptions>>().Value;
    client.BaseAddress = new Uri($"https://{opts.ApiHost}/");
});

var app = builder.Build();

app.MapControllers();

app.Run();
