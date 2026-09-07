using FullTime.Api.BetBuilder.ApiFootball;
using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.BetBuilder.OddsApi;

// EPL-only anytime-goalscorer player prop, sourced from the-odds-api's free tier - deliberately
// narrow, and independent of Providers:MarketsSource (runs regardless of whether Highlightly or
// OddsApi is the active markets source, since Highlightly has no player-prop markets of its own).
// Confirmed 2026-09-07: of the four soccer player-prop market keys the-odds-api documents, only
// player_goal_scorer_anytime actually has bookmaker coverage for EPL right now, and only from
// `us`-region books (uk/eu/au were checked and are empty for every EPL fixture tested) - see
// ODDS_API_PLAYER_PROPS_INVESTIGATION.md for the full investigation.
//
// Gating is deliberately simple, not the TTL-tiered freshness OddsApiMarketService uses: once a
// match has real prices stored, they're never refetched (a personal app showing slightly stale
// anytime-goalscorer prices is a non-issue, and the-odds-api's own docs don't promise these move
// much before kickoff anyway). If nothing's priced yet, retry at most once per day - an empty
// response costs zero quota (confirmed real 2026-09-07), so this cap isn't about quota risk, it's
// just not hammering the provider pointlessly every time someone opens the match.
public class AnytimeGoalscorerService(
    OddsApiClient oddsApi,
    ApiFootballClient apiFootball,
    AppDbContext db,
    ILogger<AnytimeGoalscorerService> logger)
{
    private const string SportKey = "soccer_epl";
    private const string MarketKey = "player_goal_scorer_anytime";
    private const string Region = "us";
    private static readonly TimeSpan RetryInterval = TimeSpan.FromDays(1);

    public async Task EnsureFreshAsync(Match match, CancellationToken ct)
    {
        if (match.LeagueId != HighlightlyLeagueMap.PremierLeague || match.Status != MatchStatus.Upcoming)
        {
            return;
        }

        var alreadyFound = await db.BetBuilderMarkets
            .AnyAsync(m => m.MatchId == match.Id && m.MarketType == MarketType.PlayerGoalscorerAnytime, ct);
        if (alreadyFound)
        {
            return;
        }

        if (match.GoalscorerPropsFetchedAt is { } lastAttempt && DateTime.UtcNow - lastAttempt < RetryInterval)
        {
            return;
        }

        match.GoalscorerPropsFetchedAt = DateTime.UtcNow;

        List<OddsApiEventDto> events;
        try
        {
            events = await oddsApi.GetEventsAsync(SportKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AnytimeGoalscorerService: failed to fetch events for {SportKey}", SportKey);
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
                "AnytimeGoalscorerService: no API-Football team id for {Home} or {Away}", resolved.HomeTeam, resolved.AwayTeam);
            await db.SaveChangesAsync(ct);
            return;
        }

        OddsApiEventOddsDto? odds;
        try
        {
            odds = await oddsApi.GetEventOddsAsync(SportKey, resolved.Id, MarketKey, regions: Region, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AnytimeGoalscorerService: failed to fetch {MarketKey} for event {EventId}", MarketKey, resolved.Id);
            await db.SaveChangesAsync(ct);
            return;
        }

        var bookmaker = odds?.Bookmakers.FirstOrDefault();
        var marketDto = bookmaker?.Markets.FirstOrDefault(m => m.Key == MarketKey);
        if (marketDto is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var homeSurnames = (await apiFootball.GetSquadPlayerNamesAsync(homeTeamId, ct)).Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var awaySurnames = (await apiFootball.GetSquadPlayerNamesAsync(awayTeamId, ct)).Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fetchedAt = DateTime.UtcNow;

        var rows = new List<BetBuilderMarket>();
        foreach (var outcome in marketDto.Outcomes.Where(o => string.Equals(o.Name, "Yes", StringComparison.OrdinalIgnoreCase)))
        {
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
                MarketType = MarketType.PlayerGoalscorerAnytime,
                Side = SelectionSide.Yes,
                Price = outcome.Price,
                PlayerName = playerName,
                Team = team,
                FetchedAt = fetchedAt,
            });
        }

        db.BetBuilderMarkets.AddRange(rows);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("AnytimeGoalscorerService: stored {Count} price(s) for match {MatchId}", rows.Count, match.Id);
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
