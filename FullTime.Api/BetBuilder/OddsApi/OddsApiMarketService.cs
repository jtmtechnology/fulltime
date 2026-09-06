using System.Globalization;
using FullTime.Api.BetBuilder.ApiFootball;
using FullTime.Api.BetBuilder;
using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.OddsApi;

// The on-demand counterpart to BetBuilderSyncService for markets/player-props, active when
// Providers:MarketsSource == "OddsApi". Called synchronously from MatchesController — never from a
// timer, per the standing no-background-polling preference. Freshness is TTL-tiered by proximity to
// kickoff, and a match is never refetched once it's no longer Upcoming (nobody can act on stale odds
// after kickoff anyway — see BetService.PlaceBetAsync), which bounds total exposure per match to its
// Upcoming window rather than indefinitely.
public class OddsApiMarketService(
    OddsApiClient oddsApi,
    ApiFootballClient apiFootball,
    AppDbContext db,
    IOptions<OddsApiOptions> options,
    ILogger<OddsApiMarketService> logger)
{
    // One HTTP call per key here (not one call with a comma-joined list) — confirmed during
    // evaluation that alternate_totals/alternate_totals_corners only return their full outcome set
    // when requested on their own, and player-prop markets are priced independently of the featured
    // ones. Run in parallel; ~9 requests per full pull, comfortably inside the 20,000/month budget
    // for a match that's only ever pulled once in its Upcoming lifetime (see the plan's quota math).
    private static readonly string[] MatchLevelMarketKeys = ["alternate_totals", "btts", "alternate_totals_corners", "correct_score"];
    private static readonly string[] PlayerPropMarketKeys = ["player_goal_scorer_anytime", "player_to_receive_card", "player_shots_on_target", "player_assists"];

    // Called from MatchesController.Upcoming — ensures the h2h price (all this endpoint shows) is
    // fresh for whatever matches that specific call actually returns, instead of a background sync
    // covering every tracked match regardless of viewership.
    public async Task EnsureH2hFreshAsync(List<Match> matches, CancellationToken ct)
    {
        var eventsBySport = new Dictionary<string, List<OddsApiEventDto>>();

        foreach (var match in matches)
        {
            if (match.Status != MatchStatus.Upcoming || !NeedsRefresh(await LatestSnapshotFetchedAtAsync(match.Id, ct), match.KickoffTime))
            {
                continue;
            }

            var sportKey = OddsApiSportMap.SportKeyFor(match.LeagueId);
            if (sportKey is null)
            {
                continue;
            }

            if (!eventsBySport.TryGetValue(sportKey, out var events))
            {
                events = await TryGetEventsAsync(sportKey, ct);
                eventsBySport[sportKey] = events;
            }

            var resolved = ResolveEvent(events, match);
            if (resolved is null)
            {
                continue;
            }

            var odds = await TryGetEventOddsAsync(sportKey, resolved.Id, "h2h", ct);
            if (odds is null)
            {
                continue;
            }

            await StoreH2hAsync(match, odds, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // Called from MatchesController.GetBetBuilderMarkets — the full 9-market pull for one match,
    // on a cache miss only.
    public async Task EnsureBetBuilderMarketsFreshAsync(Match match, CancellationToken ct)
    {
        if (match.Status != MatchStatus.Upcoming || !NeedsRefresh(match.OddsLastFetchedAt, match.KickoffTime))
        {
            return;
        }

        var sportKey = OddsApiSportMap.SportKeyFor(match.LeagueId);
        match.OddsLastFetchedAt = DateTime.UtcNow;
        if (sportKey is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var events = await TryGetEventsAsync(sportKey, ct);
        var resolved = ResolveEvent(events, match);
        if (resolved is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var allKeys = new[] { "h2h" }.Concat(MatchLevelMarketKeys).Concat(PlayerPropMarketKeys).ToArray();
        var fetchTasks = allKeys.Select(key => TryGetEventOddsAsync(sportKey, resolved.Id, key, ct));
        var responses = await Task.WhenAll(fetchTasks);

        var h2h = responses[0];
        if (h2h is not null)
        {
            await StoreH2hAsync(match, h2h, ct);
        }

        HashSet<string>? homeSurnames = null, awaySurnames = null;
        var hasPlayerProps = responses.Skip(1 + MatchLevelMarketKeys.Length).Any(r => r?.Bookmakers.Count > 0);
        if (hasPlayerProps)
        {
            homeSurnames = (await apiFootball.GetSquadPlayerNamesAsync(match.HomeTeamId, ct)).Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);
            awaySurnames = (await apiFootball.GetSquadPlayerNamesAsync(match.AwayTeamId, ct)).Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var fetchedAt = DateTime.UtcNow;
        var rows = new List<BetBuilderMarket>();

        for (var i = 0; i < MatchLevelMarketKeys.Length; i++)
        {
            var response = responses[1 + i];
            if (response is null) continue;
            rows.AddRange(ParseMatchLevelMarket(match, MatchLevelMarketKeys[i], response, fetchedAt));
        }

        for (var i = 0; i < PlayerPropMarketKeys.Length; i++)
        {
            var response = responses[1 + MatchLevelMarketKeys.Length + i];
            if (response is null) continue;
            rows.AddRange(ParsePlayerPropMarket(match, PlayerPropMarketKeys[i], response, homeSurnames, awaySurnames, fetchedAt));
        }

        // Delete-then-insert scoped to this match's non-h2h BetBuilderMarket rows — a blind
        // per-outcome upsert silently accumulated duplicates during evaluation (see
        // FullTime.Api.Sandbox's TestController, which hit and fixed exactly this bug).
        await db.BetBuilderMarkets.Where(m => m.MatchId == match.Id).ExecuteDeleteAsync(ct);
        db.BetBuilderMarkets.AddRange(rows);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Odds API: stored {Count} market row(s) for match {MatchId}", rows.Count, match.Id);
    }

    private bool NeedsRefresh(DateTime? lastFetchedAt, DateTime kickoffTime)
    {
        if (lastFetchedAt is null)
        {
            return true;
        }

        var opts = options.Value;
        var untilKickoff = kickoffTime - DateTime.UtcNow;
        var ttl = untilKickoff <= TimeSpan.FromHours(opts.ImminentBoundaryHours) ? TimeSpan.FromMinutes(opts.ImminentTtlMinutes)
            : untilKickoff <= TimeSpan.FromHours(opts.NearBoundaryHours) ? TimeSpan.FromMinutes(opts.NearTtlMinutes)
            : TimeSpan.FromHours(opts.FarTtlHours);

        return DateTime.UtcNow - lastFetchedAt.Value >= ttl;
    }

    private async Task<DateTime?> LatestSnapshotFetchedAtAsync(Guid matchId, CancellationToken ct) =>
        await db.OddsSnapshots.Where(o => o.MatchId == matchId).OrderByDescending(o => o.FetchedAt).Select(o => (DateTime?)o.FetchedAt).FirstOrDefaultAsync(ct);

    private async Task<List<OddsApiEventDto>> TryGetEventsAsync(string sportKey, CancellationToken ct)
    {
        try
        {
            return await oddsApi.GetEventsAsync(sportKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch the-odds-api events for sport {SportKey}", sportKey);
            return [];
        }
    }

    private async Task<OddsApiEventOddsDto?> TryGetEventOddsAsync(string sportKey, string eventId, string market, CancellationToken ct)
    {
        try
        {
            return await oddsApi.GetEventOddsAsync(sportKey, eventId, market, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch the-odds-api market {Market} for event {EventId}", market, eventId);
            return null;
        }
    }

    private static OddsApiEventDto? ResolveEvent(List<OddsApiEventDto> events, Match match)
    {
        var candidate = new TeamNameMatcher.MatchCandidate(match.HomeTeam, match.AwayTeam, match.KickoffTime);
        var pairs = events.Select(e => (Event: e, Candidate: new TeamNameMatcher.MatchCandidate(e.HomeTeam, e.AwayTeam, e.CommenceTime)));
        return TeamNameMatcher.FindBest(pairs, candidate)?.OddsEvent;
    }

    private async Task StoreH2hAsync(Match match, OddsApiEventOddsDto odds, CancellationToken ct)
    {
        var bookmaker = odds.Bookmakers.FirstOrDefault(b => b.Markets.Any(m => m.Key == "h2h"));
        var market = bookmaker?.Markets.FirstOrDefault(m => m.Key == "h2h");
        if (market is null)
        {
            return;
        }

        decimal? home = null, draw = null, away = null;
        foreach (var outcome in market.Outcomes)
        {
            var side = ResolveSide(outcome.Name, match.HomeTeam, match.AwayTeam);
            if (side == SelectionSide.Home) home = outcome.Price;
            else if (side == SelectionSide.Away) away = outcome.Price;
            else if (string.Equals(outcome.Name, "Draw", StringComparison.OrdinalIgnoreCase)) draw = outcome.Price;
        }

        if (home is null || draw is null || away is null)
        {
            return;
        }

        var logoUrl = BookmakerLogos.UrlForOddsApiKey(bookmaker!.Key);

        var latest = await db.OddsSnapshots.Where(o => o.MatchId == match.Id).OrderByDescending(o => o.FetchedAt).FirstOrDefaultAsync(ct);
        var changed = latest is null || latest.HomeOdds != home.Value || latest.DrawOdds != draw.Value || latest.AwayOdds != away.Value
            || !string.Equals(latest.Bookmaker, bookmaker.Title, StringComparison.OrdinalIgnoreCase)
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
            Bookmaker = bookmaker.Title,
            BookmakerLogoUrl = logoUrl,
            FetchedAt = DateTime.UtcNow,
        });
    }

    // Only the first bookmaker the-odds-api returns for this event/market — mirrors the sandbox's
    // validated "prefer the first bookmaker seen" simplification (see FullTime.Api.Sandbox's
    // AppCompatController). Looping over every returned bookmaker would store one priced row per
    // bookmaker per outcome under the same MarketType/Line/Side, making BetService.FindMarketAsync's
    // OrderByDescending(FetchedAt).FirstOrDefault() pick an arbitrary one among ties instead of a
    // single well-defined price.
    private static IEnumerable<BetBuilderMarket> ParseMatchLevelMarket(Match match, string marketKey, OddsApiEventOddsDto odds, DateTime fetchedAt)
    {
        var bookmaker = odds.Bookmakers.FirstOrDefault();
        var marketDto = bookmaker?.Markets.FirstOrDefault(m => m.Key == marketKey);
        if (marketDto is null) yield break;

        foreach (var outcome in marketDto.Outcomes)
        {
            var parsed = marketKey switch
            {
                "alternate_totals" => ParseTotals(outcome, MarketType.OverUnder),
                "alternate_totals_corners" => ParseTotals(outcome, MarketType.TotalCorners),
                "btts" => ParseYesNo(outcome, MarketType.BothTeamsToScore),
                "correct_score" => ParseCorrectScore(outcome, match.HomeTeam, match.AwayTeam),
                _ => null,
            };

            if (parsed is null) continue;

            yield return new BetBuilderMarket
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                MarketType = parsed.MarketType,
                Line = parsed.Line,
                Side = parsed.Side,
                PredictedHomeScore = parsed.PredictedHomeScore,
                PredictedAwayScore = parsed.PredictedAwayScore,
                Price = outcome.Price,
                FetchedAt = fetchedAt,
            };
        }
    }

    private static IEnumerable<BetBuilderMarket> ParsePlayerPropMarket(
        Match match, string marketKey, OddsApiEventOddsDto odds, HashSet<string>? homeSurnames, HashSet<string>? awaySurnames, DateTime fetchedAt)
    {
        var marketType = marketKey switch
        {
            "player_goal_scorer_anytime" => MarketType.PlayerGoalscorerAnytime,
            "player_to_receive_card" => MarketType.PlayerCard,
            "player_shots_on_target" => MarketType.PlayerShotsOnTarget,
            "player_assists" => MarketType.PlayerAssists,
            _ => (MarketType?)null,
        };
        if (marketType is null) yield break;

        var bookmaker = odds.Bookmakers.FirstOrDefault();
        var marketDto = bookmaker?.Markets.FirstOrDefault(m => m.Key == marketKey);
        if (marketDto is null) yield break;

        foreach (var outcome in marketDto.Outcomes)
        {
            var playerName = outcome.Description ?? outcome.Name;
            var surname = Surname(playerName);
            var team = homeSurnames?.Contains(surname) == true ? "Home"
                : awaySurnames?.Contains(surname) == true ? "Away"
                : null;

            var side = outcome.Name switch
            {
                "Yes" => SelectionSide.Yes,
                "No" => SelectionSide.No,
                "Over" => SelectionSide.Over,
                "Under" => SelectionSide.Under,
                _ => (SelectionSide?)null,
            };
            if (side is null) continue;

            yield return new BetBuilderMarket
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                MarketType = marketType.Value,
                Line = outcome.Point,
                Side = side,
                Price = outcome.Price,
                PlayerName = playerName,
                Team = team,
                FetchedAt = fetchedAt,
            };
        }
    }

    private record ParsedOutcome(MarketType MarketType, decimal? Line, SelectionSide? Side, int? PredictedHomeScore = null, int? PredictedAwayScore = null);

    private static ParsedOutcome? ParseTotals(OutcomeDto outcome, MarketType marketType)
    {
        var side = outcome.Name switch { "Over" => SelectionSide.Over, "Under" => SelectionSide.Under, _ => (SelectionSide?)null };
        return side is null || outcome.Point is null ? null : new ParsedOutcome(marketType, outcome.Point, side);
    }

    private static ParsedOutcome? ParseYesNo(OutcomeDto outcome, MarketType marketType)
    {
        var side = outcome.Name switch { "Yes" => SelectionSide.Yes, "No" => SelectionSide.No, _ => (SelectionSide?)null };
        return side is null ? null : new ParsedOutcome(marketType, null, side);
    }

    // the-odds-api packs both scores into one outcome name — "Everton:1|Manchester United:0" —
    // matched fuzzily against this match's own team names (not exact string equality: API-Football
    // and the-odds-api spell some club names differently, e.g. "Brighton" vs "Brighton and Hove
    // Albion" — see TeamNameMatcher).
    private static ParsedOutcome? ParseCorrectScore(OutcomeDto outcome, string homeTeam, string awayTeam)
    {
        var parts = outcome.Name.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;

        int? home = null, away = null;
        foreach (var part in parts)
        {
            var pieces = part.Split(':', 2);
            if (pieces.Length != 2 || !int.TryParse(pieces[1], out var score)) continue;

            var side = ResolveSide(pieces[0], homeTeam, awayTeam);
            if (side == SelectionSide.Home) home = score;
            else if (side == SelectionSide.Away) away = score;
        }

        return home is { } h && away is { } a ? new ParsedOutcome(MarketType.CorrectScore, null, null, h, a) : null;
    }

    private static SelectionSide? ResolveSide(string name, string homeTeam, string awayTeam)
    {
        var homeScore = TeamNameMatcher.Similarity(name, homeTeam);
        var awayScore = TeamNameMatcher.Similarity(name, awayTeam);
        const double threshold = 0.6;

        if (homeScore < threshold && awayScore < threshold) return null;
        return homeScore >= awayScore ? SelectionSide.Home : SelectionSide.Away;
    }

    // "J. Pickford" -> "Pickford", "Jordan Pickford" -> "Pickford" — the common ground between
    // API-Football's abbreviated squad names and the-odds-api's full ones.
    private static string Surname(string name) =>
        name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[^1] : name;
}
