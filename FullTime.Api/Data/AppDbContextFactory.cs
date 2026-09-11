using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FullTime.Api.Data;

// Lets `dotnet ef migrations add`/`database update` build just the DbContext directly, instead of
// spinning up the whole Program.cs host (which also initializes Firebase, SignalR, etc.) - without
// this, EF's design-time tooling fails on a dev machine that has no local Push:ServiceAccountPath
// file configured, even though generating a migration has nothing to do with push notifications.
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(configuration.GetConnectionString("Default"));

        return new AppDbContext(optionsBuilder.Options);
    }
}
