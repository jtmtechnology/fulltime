using System.Globalization;
using FullTime.Api.OddsApi.Dtos;

namespace FullTime.Api.OddsApi;

public record ParsedMatchOdds(decimal HomeOdds, decimal DrawOdds, decimal AwayOdds, string Bookmaker, string? BookmakerLogoUrl);

public static class MatchOddsParser
{
    public static ParsedMatchOdds? Parse(EventOddsResponse response)
    {
        var bookmakerOdds = response.Response?.Odds;
        if (bookmakerOdds is null)
        {
            return null;
        }

        var market = bookmakerOdds.Odds?.ResolvedOddsMarket
            ?? bookmakerOdds.Odds?.MatchfactMarkets.FirstOrDefault(m => m.HeaderTranslationKey == "who_will_win");

        if (market is null || market.Selections.Count != 3)
        {
            return null;
        }

        decimal? home = null, draw = null, away = null;
        foreach (var selection in market.Selections)
        {
            if (selection.Name is null || selection.OddsDecimal is null)
            {
                return null;
            }

            if (!decimal.TryParse(selection.OddsDecimal, NumberStyles.Number, CultureInfo.InvariantCulture, out var odds))
            {
                return null;
            }

            switch (selection.Name.ToLowerInvariant())
            {
                case "1":
                    home = odds;
                    break;
                case "x":
                    draw = odds;
                    break;
                case "2":
                    away = odds;
                    break;
            }
        }

        if (home is null || draw is null || away is null)
        {
            return null;
        }

        return new ParsedMatchOdds(home.Value, draw.Value, away.Value, bookmakerOdds.PersistentKey ?? "Unknown", bookmakerOdds.LogoUrl);
    }
}
