namespace FullTime.Api.BetBuilder;

// Only bet365 has a confirmed-working logo path — anything else falls back to plain text via
// BookmakerLogo.razor's own graceful degradation rather than guessing an asset URL that might not
// exist. Shared between the Bet Builder markets endpoint (single fixed bookmaker) and the 1X2
// OddsSnapshot sync (varies by match — see BetBuilderSyncService).
public static class BookmakerLogos
{
    private static readonly Dictionary<string, string> Urls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bet365"] = "https://images.fotmob.com/images/betting/bet365.png",
    };

    public static string? UrlFor(string bookmakerName) => Urls.GetValueOrDefault(bookmakerName);
}
