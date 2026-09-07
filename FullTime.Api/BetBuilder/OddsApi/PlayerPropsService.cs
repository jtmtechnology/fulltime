using FullTime.Api.BetBuilder.ApiFootball;
using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.BetBuilder.OddsApi;

// EPL-only player props (plus one match-level market, Total Corners), sourced from the-odds-api's
// free tier - deliberately narrow, and independent of Providers:MarketsSource (runs regardless of
// whether Highlightly or OddsApi is the active markets source, since neither Highlightly nor
// PlayerPropsService's own market parsing offers these otherwise). `us`-region only: uk/eu/au add
// nothing beyond it for any of these markets that hasn't been checked already - see
// ODDS_API_PLAYER_PROPS_INVESTIGATION.md for the full investigation.
//
// The two shots markets are the exception to "settled via Highlightly" below - Highlightly has no
// per-player shots data anywhere in its API (confirmed real 2026-09-07), so they still settle via
// API-Football's MatchPlayerStat instead (see PlayerStatsSettlementBackgroundService), kept wired
// in specifically for this on top of its squad-lookup use here.
//
// Gating is deliberately simple, not the TTL-tiered freshness OddsApiMarketService uses: once a
// specific market has real prices stored for a match, it's never refetched (a personal app showing
// slightly stale prices is a non-issue, and the-odds-api's own docs don't promise these move much
// before kickoff anyway). Markets are tracked independently - e.g. if goalscorer is found but cards
// isn't, cards keeps retrying on its own while goalscorer is left alone. If nothing new is priced
// yet, retry at most once an hour - an empty response costs zero quota (confirmed real
// 2026-09-07), so this cap isn't about quota risk (worst case, all four markets pricing for every
// EPL match all season is still only ~160/500 credits/month), it's just not hammering the provider
// pointlessly every time someone opens the match.
public class PlayerPropsService(
    OddsApiClient oddsApi,
    ApiFootballClient apiFootball,
    AppDbContext db,
    ILogger<PlayerPropsService> logger)
{
    private const string SportKey = "soccer_epl";
    private const string Region = "us";
    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    // YesNo: flat "player to X" list, single "Yes" outcome per player (goalscorer/cards).
    // PlayerLined: per-player Over/Under line table (assists) - see BetBuilder.razor's
    // LinedPlayerPropMarketTypes.
    // MatchLined: a plain Over/Under market with no player attribution at all (corners) - no squad
    // lookup needed, PlayerName/Team stay null on the stored row.
    private enum MarketKind { YesNo, PlayerLined, MatchLined }

    private static readonly (string Key, MarketType Type, MarketKind Kind)[] TrackedMarkets =
    [
        ("player_goal_scorer_anytime", MarketType.PlayerGoalscorerAnytime, MarketKind.YesNo),
        ("player_to_receive_card", MarketType.PlayerCard, MarketKind.YesNo),
        ("player_to_receive_red_card", MarketType.PlayerRedCard, MarketKind.YesNo),
        ("player_assists", MarketType.PlayerAssists, MarketKind.PlayerLined),
        ("alternate_totals_corners", MarketType.TotalCorners, MarketKind.MatchLined),

        // Both settled via API-Football's MatchPlayerStat (Highlightly has no per-player shots data
        // at all) - see PlayerStatsSettlementBackgroundService. player_shots_on_target has real
        // bookmaker coverage (confirmed real 2026-09-07); player_shots (total shots, not just on
        // target) had none on either fixture checked the same day, but costs nothing to keep
        // checking (empty responses are free) in case a bookmaker starts pricing it later.
        ("player_shots_on_target", MarketType.PlayerShotsOnTarget, MarketKind.PlayerLined),
        ("player_shots", MarketType.PlayerShots, MarketKind.PlayerLined),
    ];

    public async Task EnsureFreshAsync(Match match, CancellationToken ct)
    {
        if (match.LeagueId != HighlightlyLeagueMap.PremierLeague || match.Status != MatchStatus.Upcoming)
        {
            return;
        }

        var foundTypes = await db.BetBuilderMarkets
            .Where(m => m.MatchId == match.Id)
            .Select(m => m.MarketType)
            .Distinct()
            .ToListAsync(ct);
        var pending = TrackedMarkets.Where(m => !foundTypes.Contains(m.Type)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        if (match.PlayerPropsFetchedAt is { } lastAttempt && DateTime.UtcNow - lastAttempt < RetryInterval)
        {
            return;
        }

        match.PlayerPropsFetchedAt = DateTime.UtcNow;

        List<OddsApiEventDto> events;
        try
        {
            events = await oddsApi.GetEventsAsync(SportKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PlayerPropsService: failed to fetch events for {SportKey}", SportKey);
            await db.SaveChangesAsync(ct);
            return;
        }

        var resolved = ResolveEvent(events, match);
        if (resolved is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        // Keyed off the-odds-api's own team-name strings (resolved.HomeTeam/AwayTeam), not
        // match.HomeTeam/AwayTeam (Highlightly's spelling) - ApiFootballEplTeamMap's keys were built
        // from the same the-odds-api event listing, so this is an exact match, not a fuzzy one; the
        // fuzzy TeamNameMatcher step above already reconciled Highlightly's vs the-odds-api's naming
        // once, for fixture resolution only.
        if (!ApiFootballEplTeamMap.TeamIds.TryGetValue(resolved.HomeTeam, out var homeTeamId) ||
            !ApiFootballEplTeamMap.TeamIds.TryGetValue(resolved.AwayTeam, out var awayTeamId))
        {
            logger.LogWarning(
                "PlayerPropsService: no API-Football team id for {Home} or {Away}", resolved.HomeTeam, resolved.AwayTeam);
            await db.SaveChangesAsync(ct);
            return;
        }

        HashSet<string>? homeSurnames = null, awaySurnames = null;
        var fetchedAt = DateTime.UtcNow;
        var rows = new List<BetBuilderMarket>();

        foreach (var (key, type, kind) in pending)
        {
            OddsApiEventOddsDto? odds;
            try
            {
                odds = await oddsApi.GetEventOddsAsync(SportKey, resolved.Id, key, regions: Region, ct: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PlayerPropsService: failed to fetch {MarketKey} for event {EventId}", key, resolved.Id);
                continue;
            }

            var marketDto = odds?.Bookmakers.FirstOrDefault()?.Markets.FirstOrDefault(m => m.Key == key);
            if (marketDto is null)
            {
                continue;
            }

            if (kind == MarketKind.MatchLined)
            {
                foreach (var outcome in marketDto.Outcomes)
                {
                    var lineSide = outcome.Name switch
                    {
                        "Over" => SelectionSide.Over,
                        "Under" => SelectionSide.Under,
                        _ => (SelectionSide?)null,
                    };
                    if (lineSide is null || outcome.Point is null)
                    {
                        continue;
                    }

                    rows.Add(new BetBuilderMarket
                    {
                        Id = Guid.NewGuid(),
                        MatchId = match.Id,
                        MarketType = type,
                        Line = outcome.Point,
                        Side = lineSide,
                        Price = outcome.Price,
                        FetchedAt = fetchedAt,
                    });
                }
                continue;
            }

            // Squads are only fetched once real data actually turns up for the first time - no
            // point spending two API-Football calls on a match where nothing's priced yet.
            homeSurnames ??= (await apiFootball.GetSquadPlayerNamesAsync(homeTeamId, ct)).Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);
            awaySurnames ??= (await apiFootball.GetSquadPlayerNamesAsync(awayTeamId, ct)).Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var outcome in marketDto.Outcomes)
            {
                var side = outcome.Name switch
                {
                    "Yes" => SelectionSide.Yes,
                    "Over" => SelectionSide.Over,
                    "Under" => SelectionSide.Under,
                    _ => (SelectionSide?)null,
                };
                // Only-Yes markets (goalscorer/card) don't observe a "No" side in real the-odds-api
                // data, but skip it defensively rather than show a nonsensical "not to score" row if
                // a bookmaker ever adds one.
                if (side is null || (kind == MarketKind.YesNo && side != SelectionSide.Yes))
                {
                    continue;
                }

                var playerName = outcome.Description ?? outcome.Name;
                var surname = Surname(playerName);
                var team = homeSurnames.Contains(surname) ? "Home" : awaySurnames.Contains(surname) ? "Away" : null;
                if (team is null)
                {
                    continue;
                }

                rows.Add(new BetBuilderMarket
                {
                    Id = Guid.NewGuid(),
                    MatchId = match.Id,
                    MarketType = type,
                    Line = kind == MarketKind.PlayerLined ? outcome.Point : null,
                    Side = side,
                    Price = outcome.Price,
                    PlayerName = playerName,
                    Team = team,
                    FetchedAt = fetchedAt,
                });
            }
        }

        if (rows.Count > 0)
        {
            // A concurrent call (e.g. two page loads racing the fire-and-forget refresh trigger)
            // can reach this point for the same match/type before either has committed, otherwise
            // producing duplicate rows for the same market - clear out whichever types this call is
            // about to insert first, so the last writer wins instead of both surviving.
            var typesInRows = rows.Select(r => r.MarketType).Distinct().ToList();
            await db.BetBuilderMarkets
                .Where(m => m.MatchId == match.Id && typesInRows.Contains(m.MarketType))
                .ExecuteDeleteAsync(ct);

            db.BetBuilderMarkets.AddRange(rows);
            logger.LogInformation("PlayerPropsService: stored {Count} price(s) for match {MatchId}", rows.Count, match.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    private static OddsApiEventDto? ResolveEvent(List<OddsApiEventDto> events, Match match)
    {
        var candidate = new TeamNameMatcher.MatchCandidate(match.HomeTeam, match.AwayTeam, match.KickoffTime);
        var pairs = events.Select(e => (Event: e, Candidate: new TeamNameMatcher.MatchCandidate(e.HomeTeam, e.AwayTeam, e.CommenceTime)));
        return TeamNameMatcher.FindBest(pairs, candidate)?.OddsEvent;
    }

    // "J. Pickford" -> "Pickford", "Jordan Pickford" -> "Pickford" - same heuristic as
    // OddsApiMarketService.Surname (the common ground between API-Football's abbreviated squad
    // names and the-odds-api's full ones).
    private static string Surname(string name) =>
        name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[^1] : name;
}
