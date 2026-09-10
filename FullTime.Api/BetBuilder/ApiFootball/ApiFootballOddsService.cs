using System.Globalization;
using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using BookmakerLogos = FullTime.Api.BetBuilder.BookmakerLogos;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Proactive background counterpart to OddsApiMarketService, active when Providers:MarketsSource ==
// "ApiFootball" (see ApiFootballOddsSyncBackgroundService). Unlike the-odds-api (a separate call per
// market key), a single /odds?fixture= call already returns every market Bet365 prices for that
// fixture - confirmed live 2026-09-10 (105 bet types on one real upcoming fixture) - so "fresh odds
// for every tracked Upcoming match" costs one call per match per TTL tier, not per market.
public class ApiFootballOddsService(
    ApiFootballClient client,
    AppDbContext db,
    IOptions<ApiFootballOptions> options,
    ILogger<ApiFootballOddsService> logger)
{
    public async Task EnsureBetBuilderMarketsFreshAsync(Match match, CancellationToken ct)
    {
        if (match.Status != MatchStatus.Upcoming || !NeedsRefresh(match.ApiFootballOddsLastFetchedAt, match.KickoffTime))
        {
            return;
        }

        // Stamped even on a miss below, same reasoning as OddsApiMarketService.OddsLastFetchedAt -
        // a match with no priced markets yet still needs a TTL floor to compare against.
        match.ApiFootballOddsLastFetchedAt = DateTime.UtcNow;

        List<OddsFixtureResponseDto> response;
        try
        {
            response = await client.GetOddsAsync(long.Parse(match.ExternalId), options.Value.OddsBookmakerId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch API-Football odds for match {MatchId}", match.Id);
            await db.SaveChangesAsync(ct);
            return;
        }

        var bookmaker = response.FirstOrDefault()?.Bookmakers.FirstOrDefault();
        if (bookmaker is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var fetchedAt = DateTime.UtcNow;

        // Match-card-level Home/Draw/Away odds (OddsSnapshot) are a separate display path from
        // BetBuilderMarket - mirrors BetBuilderSyncService.SnapshotOneXTwoIfChangedAsync exactly
        // (found missing 2026-09-10: this cutover only ever wrote BetBuilderMarket rows, leaving
        // match cards with no basic odds at all once Highlightly's own snapshot sync stopped
        // running).
        await SnapshotMatchResultIfChangedAsync(match, bookmaker, fetchedAt, ct);

        var rows = bookmaker.Bets.SelectMany(bet => ParseBet(match, bet, fetchedAt)).ToList();

        // Delete-then-insert scoped to this match - a blind per-outcome upsert would silently
        // accumulate duplicates across refresh cycles, same discipline OddsApiMarketService and
        // BetBuilderSyncService both already require (no DB uniqueness constraint protects against
        // this).
        await db.BetBuilderMarkets.Where(m => m.MatchId == match.Id).ExecuteDeleteAsync(ct);
        db.BetBuilderMarkets.AddRange(rows);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("API-Football odds: stored {Count} market row(s) for match {MatchId}", rows.Count, match.Id);
    }

    // Mirrors BetBuilderSyncService.SnapshotOneXTwoIfChangedAsync exactly - only writes a new row
    // when the price/bookmaker actually changed, so OddsSnapshot doesn't accumulate a row every
    // single tick regardless of whether anything moved.
    private async Task SnapshotMatchResultIfChangedAsync(Match match, BookmakerOddsDto bookmaker, DateTime fetchedAt, CancellationToken ct)
    {
        var matchWinner = bookmaker.Bets.FirstOrDefault(b => b.Name == "Match Winner");
        if (matchWinner is null)
        {
            return;
        }

        decimal? home = null, draw = null, away = null;
        foreach (var value in matchWinner.Values)
        {
            if (!decimal.TryParse(value.Odd, NumberStyles.Number, CultureInfo.InvariantCulture, out var odd))
            {
                continue;
            }

            switch (value.Value)
            {
                case "Home": home = odd; break;
                case "Draw": draw = odd; break;
                case "Away": away = odd; break;
            }
        }

        if (home is null || draw is null || away is null)
        {
            return;
        }

        var latest = await db.OddsSnapshots
            .Where(o => o.MatchId == match.Id)
            .OrderByDescending(o => o.FetchedAt)
            .FirstOrDefaultAsync(ct);

        var logoUrl = BookmakerLogos.UrlFor(bookmaker.Name);
        var changed = latest is null
            || latest.HomeOdds != home.Value
            || latest.DrawOdds != draw.Value
            || latest.AwayOdds != away.Value
            || !string.Equals(latest.Bookmaker, bookmaker.Name, StringComparison.OrdinalIgnoreCase)
            || latest.BookmakerLogoUrl != logoUrl;

        if (!changed)
        {
            return;
        }

        db.OddsSnapshots.Add(new OddsSnapshot
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            HomeOdds = home.Value,
            DrawOdds = draw.Value,
            AwayOdds = away.Value,
            Bookmaker = bookmaker.Name,
            BookmakerLogoUrl = logoUrl,
            FetchedAt = fetchedAt,
        });
    }

    private bool NeedsRefresh(DateTime? lastFetchedAt, DateTime kickoffTime)
    {
        if (lastFetchedAt is null)
        {
            return true;
        }

        var opts = options.Value;
        var untilKickoff = kickoffTime - DateTime.UtcNow;
        var ttl = untilKickoff <= TimeSpan.FromHours(opts.OddsImminentBoundaryHours) ? TimeSpan.FromMinutes(opts.OddsImminentTtlMinutes)
            : untilKickoff <= TimeSpan.FromHours(opts.OddsNearBoundaryHours) ? TimeSpan.FromMinutes(opts.OddsNearTtlMinutes)
            : TimeSpan.FromHours(opts.OddsFarTtlHours);

        return DateTime.UtcNow - lastFetchedAt.Value >= ttl;
    }

    private static IEnumerable<BetBuilderMarket> ParseBet(Match match, BetOddsDto bet, DateTime fetchedAt)
    {
        foreach (var value in bet.Values)
        {
            var parsed = ParseOutcome(bet.Name, value.Value);
            if (parsed is null || !decimal.TryParse(value.Odd, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                continue;
            }

            yield return new BetBuilderMarket
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                MarketType = parsed.MarketType,
                Line = parsed.Line,
                Side = parsed.Side,
                PredictedHomeScore = parsed.PredictedHomeScore,
                PredictedAwayScore = parsed.PredictedAwayScore,
                Price = price,
                PlayerName = parsed.PlayerName,
                Team = parsed.Team,
                FetchedAt = fetchedAt,
            };
        }
    }

    private sealed record ParsedOutcome(
        MarketType MarketType, decimal? Line = null, SelectionSide? Side = null,
        int? PredictedHomeScore = null, int? PredictedAwayScore = null, string? PlayerName = null, string? Team = null);

    // Bet365-via-API-Football bet names + value shapes confirmed live 2026-09-10 against a real
    // upcoming fixture (105 bet types returned; these are the ones matching MarketType). Team-scoped
    // names (Home/Away prefix) give team attribution for free, sidestepping the surname/squad-lookup
    // OddsApiMarketService needs for the-odds-api. Two markets the original plan assumed would exist
    // deliberately aren't mapped here, same tolerant "return null, skip" pattern as everywhere else
    // in this codebase (BetBuilderSyncService.ParseOutcome, OddsApiMarketService.ParsePlayerPropMarket):
    // "Player Assists" turned out to be a whole-match Yes/No proposition under this bookmaker, not
    // per-player, so it can't drive a PlayerAssists pick; no per-player card market exists at all
    // (PlayerCard/PlayerRedCard just never get rows here - same real gap the-odds-api already had,
    // see HANDOVER.md §7.4/§6.13).
    private static ParsedOutcome? ParseOutcome(string betName, string value) => betName switch
    {
        "Match Winner" => ParseMatchResult(value),
        "Goals Over/Under" => ParseLine(value, MarketType.OverUnder),
        "Both Teams Score" => ParseYesNo(value, MarketType.BothTeamsToScore),
        "Exact Score" => ParseCorrectScore(value),
        "Team To Score First" => ParseFirstTeamToScore(value),
        "Home Corners Over/Under" => ParseTeamLine(value, MarketType.TeamCorners, "Home"),
        "Away Corners Over/Under" => ParseTeamLine(value, MarketType.TeamCorners, "Away"),
        "Home Team Total Cards" => ParseTeamLine(value, MarketType.TeamCards, "Home"),
        "Away Team Total Cards" => ParseTeamLine(value, MarketType.TeamCards, "Away"),
        "Home Anytime Goal Scorer" => ParsePlayerYes(value, MarketType.PlayerGoalscorerAnytime, "Home"),
        "Away Anytime Goal Scorer" => ParsePlayerYes(value, MarketType.PlayerGoalscorerAnytime, "Away"),
        "Home Player Shots" => ParsePlayerLadder(value, MarketType.PlayerShots, "Home"),
        "Away Player Shots" => ParsePlayerLadder(value, MarketType.PlayerShots, "Away"),
        "Home Player Shots On Target Total" => ParsePlayerLadder(value, MarketType.PlayerShotsOnTarget, "Home"),
        "Away Player Shots On Target Total" => ParsePlayerLadder(value, MarketType.PlayerShotsOnTarget, "Away"),
        "Player Fouls Committed" => ParsePlayerLadder(value, MarketType.PlayerFoulsCommitted, null),
        _ => null,
    };

    private static ParsedOutcome? ParseMatchResult(string value) => value switch
    {
        "Home" => new ParsedOutcome(MarketType.MatchResult, Side: SelectionSide.Home),
        "Draw" => new ParsedOutcome(MarketType.MatchResult, Side: SelectionSide.Draw),
        "Away" => new ParsedOutcome(MarketType.MatchResult, Side: SelectionSide.Away),
        _ => null,
    };

    private static ParsedOutcome? ParseFirstTeamToScore(string value) => value switch
    {
        "Home" => new ParsedOutcome(MarketType.FirstTeamToScore, Side: SelectionSide.Home),
        "Away" => new ParsedOutcome(MarketType.FirstTeamToScore, Side: SelectionSide.Away),
        "No goal" => new ParsedOutcome(MarketType.FirstTeamToScore, Side: SelectionSide.None),
        _ => null,
    };

    private static ParsedOutcome? ParseYesNo(string value, MarketType marketType) => value switch
    {
        "Yes" => new ParsedOutcome(marketType, Side: SelectionSide.Yes),
        "No" => new ParsedOutcome(marketType, Side: SelectionSide.No),
        _ => null,
    };

    // "Over 2.5" / "Under 2.5" -> (Over, 2.5). The quarter/Asian-handicap lines ("Over 2.25" etc.)
    // live under the separate "Goal Line" bet name, which isn't requested here, so this only ever
    // sees clean half-lines in practice.
    private static ParsedOutcome? ParseLine(string value, MarketType marketType)
    {
        var parts = value.Split(' ', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        var side = parts[0] switch { "Over" => SelectionSide.Over, "Under" => SelectionSide.Under, _ => (SelectionSide?)null };
        return side is null || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var line)
            ? null
            : new ParsedOutcome(marketType, line, side);
    }

    private static ParsedOutcome? ParseTeamLine(string value, MarketType marketType, string team)
    {
        var parsed = ParseLine(value, marketType);
        return parsed is null ? null : parsed with { Team = team };
    }

    // "1:0" -> (1, 0) - same colon-packed-score convention API-Football uses everywhere (see
    // ApiFootballMatchSyncService's own score parsing).
    private static ParsedOutcome? ParseCorrectScore(string value)
    {
        var parts = value.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[0], out var home) && int.TryParse(parts[1], out var away)
            ? new ParsedOutcome(MarketType.CorrectScore, PredictedHomeScore: home, PredictedAwayScore: away)
            : null;
    }

    // Anytime-goalscorer values are just the player's full name, one row per player - only a
    // backable "Yes" price is ever offered (no explicit "No" wording, no per-player "No" price).
    private static ParsedOutcome ParsePlayerYes(string value, MarketType marketType, string team) =>
        new(marketType, Side: SelectionSide.Yes, PlayerName: value, Team: team);

    // "Matheus Cunha - 2" -> player "Matheus Cunha", line 1.5 (Over) - one row per count offered,
    // each a genuinely distinct backable Over-N.5 price. No matching Under exists for this market
    // shape (unlike Goals Over/Under's two-sided lines) - that's a real asymmetry in how this
    // bookmaker prices player props, not a parsing gap.
    private static ParsedOutcome? ParsePlayerLadder(string value, MarketType marketType, string? team)
    {
        var idx = value.LastIndexOf(" - ", StringComparison.Ordinal);
        if (idx < 0 || !int.TryParse(value[(idx + 3)..], out var count))
        {
            return null;
        }

        return new ParsedOutcome(marketType, count - 0.5m, SelectionSide.Over, PlayerName: value[..idx], Team: team);
    }
}
