namespace FullTime.Api.BetBuilder;

// Only these bookmaker names have a confirmed-working fotmob logo path (checked one by one against
// Highlightly's own bookmaker name strings — most guessed slugs 403). Anything else falls back to
// plain text via BookmakerLogo.razor's own graceful degradation rather than guessing an asset URL
// that might not exist. Shared between the Bet Builder markets endpoint (single fixed bookmaker)
// and the 1X2 OddsSnapshot sync, where BetBuilderSyncService only picks a bookmaker from this set
// so every displayed price also has a logo.
public static class BookmakerLogos
{
    private static readonly Dictionary<string, string> Urls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bet365"] = "https://images.fotmob.com/images/betting/bet365.png",
        ["10bet"] = "https://images.fotmob.com/images/betting/10bet.png",
        ["22Bet"] = "https://images.fotmob.com/images/betting/22bet.png",
        ["Betano"] = "https://images.fotmob.com/images/betting/betano.png",
        ["Betway"] = "https://images.fotmob.com/images/betting/betway.png",
        ["Bwin"] = "https://images.fotmob.com/images/betting/bwin.png",
        ["Interwetten"] = "https://images.fotmob.com/images/betting/interwetten.png",
        ["Mostbet"] = "https://images.fotmob.com/images/betting/mostbet.png",
        ["Novibet"] = "https://images.fotmob.com/images/betting/novibet.png",
        ["Parimatch"] = "https://images.fotmob.com/images/betting/parimatch.png",
        ["Unibet"] = "https://images.fotmob.com/images/betting/unibet.png",
    };

    public static string? UrlFor(string bookmakerName) => Urls.GetValueOrDefault(bookmakerName);

    public static bool HasLogo(string bookmakerName) => Urls.ContainsKey(bookmakerName);
}
