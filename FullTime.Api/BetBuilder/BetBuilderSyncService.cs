using System.Globalization;
using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

// The only caller of RefreshAsync is BetBuilderSyncBackgroundService's timer — same choke-point
// pattern as MatchSyncService for the primary odds/scores provider.
public class BetBuilderSyncService(
    HighlightlyClient client,
    AppDbContext db,
    IOptions<HighlightlyOptions> options,
    ILogger<BetBuilderSyncService> logger)
{
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await SyncMatchesAndOddsAsync(ct);
        await ResolveFirstGoalScorersAsync(ct);
    }

    private async Task SyncMatchesAndOddsAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = DateTime.UtcNow.AddDays(opts.MatchWindowDays);

        var candidateMatches = await db.Matches
            .Where(m => m.Status == MatchStatus.Upcoming && m.KickoffTime <= horizon)
            .ToListAsync(ct);

        var relevantMatches = candidateMatches
            .Where(m => HighlightlyLeagueMap.LeagueIds.ContainsKey(m.LeagueId))
            .ToList();

        if (relevantMatches.Count == 0)
        {
            return;
        }

        // Cache one /football/matches call per (leagueId, date) pair — several of our own LeagueId
        // variants (e.g. the UEFA-qualifying temp pool) can map to the same Highlightly league id
        // and the same date, so this avoids re-fetching identical pages.
        var matchesCache = new Dictionary<(int LeagueId, DateOnly Date), List<MatchDto>>();

        async Task<List<MatchDto>> GetCachedMatchesAsync(int highlightlyLeagueId, DateOnly date)
        {
            var key = (highlightlyLeagueId, date);
            if (!matchesCache.TryGetValue(key, out var cached))
            {
                cached = await client.GetMatchesAsync(highlightlyLeagueId, SeasonFor(date), date, ct);
                matchesCache[key] = cached;
            }

            return cached;
        }

        var matchedCount = 0;
        foreach (var match in relevantMatches.Where(m => m.HighlightlyMatchId is null))
        {
            var date = DateOnly.FromDateTime(match.KickoffTime);

            foreach (var highlightlyLeagueId in HighlightlyLeagueMap.LeagueIds[match.LeagueId])
            {
                var candidates = await GetCachedMatchesAsync(highlightlyLeagueId, date);
                var found = FindMatchingMatch(match, candidates);
                if (found is null)
                {
                    continue;
                }

                match.HighlightlyMatchId = found.Id;
                match.HighlightlyLeagueId = highlightlyLeagueId;
                match.HighlightlyHomeTeamId = found.HomeTeam.Id;
                match.HighlightlyAwayTeamId = found.AwayTeam.Id;
                matchedCount++;
                break;
            }
        }

        if (matchedCount > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var matchedMatches = relevantMatches.Where(m => m.HighlightlyMatchId is not null).ToList();
        if (matchedMatches.Count == 0)
        {
            return;
        }

        // Odds are fetched per (leagueId, date) too, paginated — cheaper than one call per match
        // since the provider returns several matches per page (still capped at 5, so a busy
        // matchday needs a few pages).
        var oddsKeys = matchedMatches
            .Select(m => (LeagueId: m.HighlightlyLeagueId!.Value, Date: DateOnly.FromDateTime(m.KickoffTime)))
            .Distinct()
            .ToList();

        var matchByHighlightlyId = matchedMatches.ToDictionary(m => m.HighlightlyMatchId!.Value);
        var fetchedAt = DateTime.UtcNow;
        var storedCount = 0;

        foreach (var (leagueId, date) in oddsKeys)
        {
            var offset = 0;
            while (true)
            {
                var page = await client.GetOddsAsync(leagueId, date, opts.BookmakerName, offset, ct);
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
            "Bet-builder sync: matched {MatchCount} match(es) to Highlightly, stored {MarketCount} market row(s)",
            matchedMatches.Count, storedCount);
    }

    // Resolves MarketType.FirstTeamToScore, which can't be derived from the final score alone —
    // needs the goal timeline from Highlightly's own event data. A 0-0 result needs no external
    // call at all (nobody scored, so "None" is certain); any other finished match needs one
    // /football/events lookup. Left Pending (retried next tick) if the provider hasn't backfilled
    // events for that match yet.
    private async Task ResolveFirstGoalScorersAsync(CancellationToken ct)
    {
        var candidates = await db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.HighlightlyMatchId != null && m.FirstGoalScorerSide == null)
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

            var events = await client.GetEventsAsync(match.HighlightlyMatchId!.Value, ct);
            var firstGoal = events
                .Where(e => e.Type == "Goal")
                .OrderBy(e => ParseMinute(e.Time))
                .FirstOrDefault();

            if (firstGoal is null)
            {
                continue;
            }

            match.FirstGoalScorerSide = firstGoal.Team.Id == match.HighlightlyHomeTeamId
                ? SelectionSide.Home
                : firstGoal.Team.Id == match.HighlightlyAwayTeamId
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

    // European domestic/UEFA seasons run August–May and are numbered by their starting year.
    private static int SeasonFor(DateOnly date) => date.Month >= 7 ? date.Year : date.Year - 1;

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

    private static MatchDto? FindMatchingMatch(Match match, IEnumerable<MatchDto> candidates)
    {
        var matchDate = DateOnly.FromDateTime(match.KickoffTime);

        return candidates.FirstOrDefault(c =>
            DateOnly.FromDateTime(c.Date) == matchDate
            && TeamNamesMatch(match.HomeTeam, c.HomeTeam.Name)
            && TeamNamesMatch(match.AwayTeam, c.AwayTeam.Name));
    }

    // Word-by-word prefix matching rather than raw substring containment on the whole squashed
    // name — the latter fails on common abbreviations our primary provider uses that Highlightly
    // doesn't (e.g. "Man United" vs "Manchester United": "manunited" is not a substring of
    // "manchesterunited" in either direction, but "man" is a prefix of "manchester" and "united"
    // matches "united" word-for-word). Every word of the shorter name needs a prefix match against
    // some word of the longer name.
    private static bool TeamNamesMatch(string ours, string theirs)
    {
        var a = NormalizeTeamWords(ours);
        var b = NormalizeTeamWords(theirs);
        if (a.Count == 0 || b.Count == 0)
        {
            return false;
        }

        var (shorter, longer) = a.Count <= b.Count ? (a, b) : (b, a);
        return shorter.All(word => longer.Any(other => other.StartsWith(word, StringComparison.Ordinal) || word.StartsWith(other, StringComparison.Ordinal)));
    }

    private static List<string> NormalizeTeamWords(string name)
    {
        var words = name.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .ToList();

        if (words.Count > 0)
        {
            var last = words[^1];
            foreach (var suffix in new[] { "fc", "afc", "cf", "cfc" })
            {
                if (last.EndsWith(suffix, StringComparison.Ordinal) && last.Length > suffix.Length)
                {
                    words[^1] = last[..^suffix.Length];
                    break;
                }
            }
        }

        return words;
    }
}
