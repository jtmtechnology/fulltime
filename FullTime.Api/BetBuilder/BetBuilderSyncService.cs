using System.Globalization;
using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

// The only caller of RefreshAsync is BetBuilderSyncBackgroundService's timer — same choke-point
// pattern as HighlightlyMatchSyncService for the primary match/score sync.
public class BetBuilderSyncService(
    HighlightlyClient client,
    AppDbContext db,
    IOptions<HighlightlyOptions> options,
    ILogger<BetBuilderSyncService> logger)
{
    // Returns whether any Highlightly-tracked match was found to price. False is a signal to the
    // caller (BetBuilderSyncBackgroundService) to retry soon rather than wait its normal, much
    // longer interval — the only realistic cause is a cold-start race against
    // HighlightlyMatchSyncService's own first tick, since there's no second provider to reconcile
    // against any more (see HighlightlyMatchSyncService's own comment for the full context).
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var foundMatches = await SyncMatchesAndOddsAsync(ct);
        await ResolveFirstGoalScorersAsync(ct);
        return foundMatches;
    }

    private async Task<bool> SyncMatchesAndOddsAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var horizon = DateTime.UtcNow.AddDays(opts.MatchWindowDays);

        var candidateMatches = await db.Matches
            .Where(m => m.Status == MatchStatus.Upcoming && m.KickoffTime <= horizon)
            .ToListAsync(ct);

        // Matches are synced directly from Highlightly (see HighlightlyMatchSyncService), so
        // LeagueId/ExternalId/HomeTeamId/AwayTeamId are already Highlightly's own IDs — no
        // reconciliation against a second provider needed.
        var relevantMatches = candidateMatches
            .Where(m => HighlightlyLeagueMap.TrackedLeagueIds.Contains(m.LeagueId))
            .ToList();

        if (relevantMatches.Count == 0)
        {
            return false;
        }

        // Odds are fetched per (leagueId, date), paginated — cheaper than one call per match since
        // the provider returns several matches per page (still capped at 5, so a busy matchday
        // needs a few pages).
        var oddsKeys = relevantMatches
            .Select(m => (LeagueId: (int)m.LeagueId, Date: DateOnly.FromDateTime(m.KickoffTime)))
            .Distinct()
            .ToList();

        var matchByHighlightlyId = relevantMatches.ToDictionary(m => long.Parse(m.ExternalId));
        var fetchedAt = DateTime.UtcNow;
        var storedCount = 0;

        foreach (var (leagueId, date) in oddsKeys)
        {
            var offset = 0;
            while (true)
            {
                // Fetched unfiltered (every bookmaker) in one call — cheaper than a second
                // bookmaker-filtered call just for the 1X2 price, and SnapshotOneXTwoIfChangedAsync
                // needs to see every bookmaker anyway.
                var page = await client.GetOddsAsync(leagueId, date, offset, ct: ct);
                if (page is null || page.Data.Count == 0)
                {
                    break;
                }

                foreach (var matchOdds in page.Data)
                {
                    if (!matchByHighlightlyId.TryGetValue(matchOdds.MatchId, out var match))
                    {
                        continue;
                    }

                    // 1X2: whichever bookmaker's "Full Time Result" entry appears first for this
                    // match, restricted to BookmakerLogos' confirmed set so every displayed price
                    // also has a logo — deliberately not pinned to a single opts.BookmakerName like
                    // the other markets, so the price shown still varies by match (confirmed via a
                    // real odds page that the first-listed bookmaker varies match to match).
                    var fullTimeResult = matchOdds.Odds.FirstOrDefault(o =>
                        o.Market == "Full Time Result" && BookmakerLogos.HasLogo(o.BookmakerName));
                    if (fullTimeResult is not null)
                    {
                        await SnapshotOneXTwoIfChangedAsync(match, fullTimeResult, fetchedAt, ct);
                    }

                    foreach (var entry in matchOdds.Odds)
                    {
                        if (!string.Equals(entry.BookmakerName, opts.BookmakerName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        foreach (var value in entry.Values)
                        {
                            var outcome = ParseOutcome(entry.Market, value.Value);
                            if (outcome is null)
                            {
                                continue;
                            }

                            await UpsertMarketAsync(match.Id, outcome, value.Odd, fetchedAt, ct);
                            storedCount++;
                        }
                    }
                }

                offset += page.Data.Count;
                if (page.Pagination is null || offset >= page.Pagination.TotalCount)
                {
                    break;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Bet-builder sync: stored {MarketCount} market row(s) across {MatchCount} match(es)",
            storedCount, relevantMatches.Count);
        return true;
    }

    private async Task SnapshotOneXTwoIfChangedAsync(Match match, OddsEntryDto entry, DateTime fetchedAt, CancellationToken ct)
    {
        decimal? home = null, draw = null, away = null;
        foreach (var value in entry.Values)
        {
            switch (value.Value)
            {
                case "Home": home = value.Odd; break;
                case "Draw": draw = value.Odd; break;
                case "Away": away = value.Odd; break;
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

        var logoUrl = BookmakerLogos.UrlFor(entry.BookmakerName);

        // Includes BookmakerLogoUrl even though it's derived purely from Bookmaker — a row written
        // before BookmakerLogos gained an entry for this bookmaker would otherwise stay logo-less
        // forever once the price and bookmaker themselves settle down and stop changing tick to
        // tick (confirmed happening in production: several matches stuck on a null logo despite
        // their bookmaker having a confirmed one by the time this was investigated).
        var changed = latest is null
            || latest.HomeOdds != home.Value
            || latest.DrawOdds != draw.Value
            || latest.AwayOdds != away.Value
            || !string.Equals(latest.Bookmaker, entry.BookmakerName, StringComparison.OrdinalIgnoreCase)
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
            Bookmaker = entry.BookmakerName,
            BookmakerLogoUrl = logoUrl,
            FetchedAt = fetchedAt,
        });
    }

    // Resolves MarketType.FirstTeamToScore, which can't be derived from the final score alone —
    // needs the goal timeline from Highlightly's own event data. A 0-0 result needs no external
    // call at all (nobody scored, so "None" is certain); any other finished match needs one
    // /football/events lookup. Left Pending (retried next tick) if the provider hasn't backfilled
    // events for that match yet — but only for matches that finished recently. Confirmed in
    // production that some matches (e.g. smaller continental/qualifying fixtures) get an empty
    // events array permanently, never just "not yet backfilled" — without this cutoff, every one of
    // those gets re-queried every single tick, forever, and the pile only grows daily. A
    // FirstTeamToScore pick on a match whose events never backfill was already stuck Pending either
    // way (see SettlementService.ResolvePicksAsync's guard), so aging out the retry doesn't change
    // any settlement outcome — it just stops paying quota to reconfirm "still unknown".
    private async Task ResolveFirstGoalScorersAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-3);
        var candidates = await db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.FirstGoalScorerSide == null && m.KickoffTime >= cutoff)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return;
        }

        var resolvedCount = 0;

        foreach (var match in candidates)
        {
            if (match.HomeScore == 0 && match.AwayScore == 0)
            {
                match.FirstGoalScorerSide = SelectionSide.None;
                resolvedCount++;
                continue;
            }

            var events = await client.GetEventsAsync(long.Parse(match.ExternalId), ct);
            var firstGoal = events
                .Where(e => e.Type == "Goal")
                .OrderBy(e => ParseMinute(e.Time))
                .FirstOrDefault();

            if (firstGoal is null)
            {
                continue;
            }

            match.FirstGoalScorerSide = firstGoal.Team.Id == match.HomeTeamId
                ? SelectionSide.Home
                : firstGoal.Team.Id == match.AwayTeamId
                    ? SelectionSide.Away
                    : null;

            if (match.FirstGoalScorerSide is not null)
            {
                resolvedCount++;
            }
        }

        if (resolvedCount == 0)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Resolved first goalscorer for {Count} match(es)", resolvedCount);
    }

    // Event minutes come as "45" or "45+1"/"90+7" (stoppage time) — split on '+' and sort by
    // (base, added) so stoppage-time goals still order correctly after their half's regular time.
    private static (int Base, int Added) ParseMinute(string time)
    {
        var parts = time.Split('+');
        var baseMinute = int.TryParse(parts[0], out var b) ? b : int.MaxValue;
        var added = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : 0;
        return (baseMinute, added);
    }

    private record ParsedOutcome(MarketType MarketType, decimal? Line, SelectionSide? Side, int? PredictedHomeScore, int? PredictedAwayScore);

    // Maps one Highlightly odds "market" + "value" pair to our market model. Only the four market
    // types the user picked are recognised here — everything else (Asian Handicap, Clean Sheet,
    // Odd/Even, Total Corners, Full Time Result, etc.) returns null and is silently skipped.
    private static ParsedOutcome? ParseOutcome(string market, string value)
    {
        if (market.StartsWith("Total Goals ", StringComparison.Ordinal))
        {
            var lineText = market["Total Goals ".Length..];
            if (!decimal.TryParse(lineText, NumberStyles.Number, CultureInfo.InvariantCulture, out var line) || !IsHalfLine(line))
            {
                return null;
            }

            var side = value switch { "Over" => SelectionSide.Over, "Under" => SelectionSide.Under, _ => (SelectionSide?)null };
            return side is null ? null : new ParsedOutcome(MarketType.OverUnder, line, side, null, null);
        }

        if (market == "Both Teams To Score")
        {
            var side = value switch { "Yes" => SelectionSide.Yes, "No" => SelectionSide.No, _ => (SelectionSide?)null };
            return side is null ? null : new ParsedOutcome(MarketType.BothTeamsToScore, null, side, null, null);
        }

        if (market == "First Team To Score")
        {
            var side = value switch
            {
                "Home" => SelectionSide.Home,
                "Away" => SelectionSide.Away,
                "None" => SelectionSide.None,
                _ => (SelectionSide?)null,
            };
            return side is null ? null : new ParsedOutcome(MarketType.FirstTeamToScore, null, side, null, null);
        }

        if (market.StartsWith("Correct Score ", StringComparison.Ordinal))
        {
            var parts = market["Correct Score ".Length..].Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var home) && int.TryParse(parts[1], out var away))
            {
                return new ParsedOutcome(MarketType.CorrectScore, null, null, home, away);
            }
        }

        return null;
    }

    // Half-lines only (goals are integers, so a half-line bet can never push) — quarter/whole lines
    // are skipped entirely rather than modelled, since split-stake/push settlement isn't built.
    private static bool IsHalfLine(decimal value) => value % 1 != 0 && (value * 2) % 1 == 0;

    private async Task UpsertMarketAsync(Guid matchId, ParsedOutcome outcome, decimal price, DateTime fetchedAt, CancellationToken ct)
    {
        var existing = await db.BetBuilderMarkets.FirstOrDefaultAsync(m =>
            m.MatchId == matchId && m.MarketType == outcome.MarketType && m.Line == outcome.Line && m.Side == outcome.Side
            && m.PredictedHomeScore == outcome.PredictedHomeScore && m.PredictedAwayScore == outcome.PredictedAwayScore, ct);

        if (existing is null)
        {
            db.BetBuilderMarkets.Add(new BetBuilderMarket
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                MarketType = outcome.MarketType,
                Line = outcome.Line,
                Side = outcome.Side,
                PredictedHomeScore = outcome.PredictedHomeScore,
                PredictedAwayScore = outcome.PredictedAwayScore,
                Price = price,
                FetchedAt = fetchedAt,
            });
            return;
        }

        existing.Price = price;
        existing.FetchedAt = fetchedAt;
    }
}
