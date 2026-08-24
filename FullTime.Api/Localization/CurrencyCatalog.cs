namespace FullTime.Api.Localization;

// Deliberately cosmetic — swaps the displayed symbol per user's own country, not a real currency
// conversion (no FX rate is applied anywhere, so a leaderboard mixing currencies just shows the same
// raw numbers with different labels). A user with no country set, or one outside this curated list,
// falls back to £ — the symbol every user's amounts were hardcoded to before this field existed.
public static class CurrencyCatalog
{
    public const string DefaultSymbol = "£";

    private static readonly Dictionary<string, string> SymbolsByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = "£", ["IE"] = "€",
        ["US"] = "$", ["CA"] = "$", ["AU"] = "$", ["NZ"] = "$",
        ["FR"] = "€", ["DE"] = "€", ["ES"] = "€", ["IT"] = "€", ["PT"] = "€", ["NL"] = "€",
        ["BE"] = "€", ["AT"] = "€", ["FI"] = "€", ["GR"] = "€",
        ["JP"] = "¥", ["IN"] = "₹", ["ZA"] = "R", ["CH"] = "CHF",
    };

    public static string SymbolFor(string? countryCode) =>
        countryCode is not null && SymbolsByCountry.TryGetValue(countryCode, out var symbol) ? symbol : DefaultSymbol;

    // For a Profile country picker — display name shown to the user, code stored and sent to the API.
    public static readonly (string Code, string Name)[] Options =
    [
        ("GB", "United Kingdom"), ("IE", "Ireland"),
        ("US", "United States"), ("CA", "Canada"), ("AU", "Australia"), ("NZ", "New Zealand"),
        ("FR", "France"), ("DE", "Germany"), ("ES", "Spain"), ("IT", "Italy"), ("PT", "Portugal"),
        ("NL", "Netherlands"), ("BE", "Belgium"), ("AT", "Austria"), ("FI", "Finland"), ("GR", "Greece"),
        ("JP", "Japan"), ("IN", "India"), ("ZA", "South Africa"), ("CH", "Switzerland"),
    ];
}
