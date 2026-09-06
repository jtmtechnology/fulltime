using FullTime.Api.Sandbox.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Sandbox.Controllers;

// Mimics just enough of FullTime.Api's real /api/matches surface (see FullTime.Api/Controllers/
// MatchesController.cs) for the real FullTime.App.Shared UI to render against this sandbox
// unmodified - built to let the emulator point ApiConfig.BaseUrl here and show real API-Football
// live scores + the-odds-api player props without touching production. Response shapes must match
// FullTime.App.Shared/Models/ApiModels.cs exactly (UpcomingMatchDto, BetBuilderMarketsResponse).
[ApiController]
[Route("api")]
public class AppCompatController(SandboxDbContext db) : ControllerBase
{
    [HttpGet("config")]
    public IActionResult Config() => Ok(new { RefreshIntervalSeconds = 30 });

    [HttpGet("matches/upcoming")]
    public async Task<IActionResult> Upcoming([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var query = db.Matches.AsQueryable();
        if (date is { } d)
        {
            var start = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = start.AddDays(1);
            query = query.Where(m => m.KickoffTime >= start && m.KickoffTime < end);
        }

        var matches = await query.OrderBy(m => m.KickoffTime).ToListAsync(ct);
        var hasPropsFor = (await db.PlayerPropMarkets.Select(p => p.MatchId).Distinct().ToListAsync(ct)).ToHashSet();
        var h2hRows = await db.PlayerPropMarkets.Where(p => p.MarketKey == "h2h").ToListAsync(ct);
        var h2hByMatch = h2hRows.GroupBy(p => p.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var dtos = matches.Select(m =>
        {
            h2hByMatch.TryGetValue(m.Id, out var h2h);
            return new
            {
                m.Id,
                m.LeagueId,
                m.HomeTeam,
                m.AwayTeam,
                HomeLogoUrl = m.HomeTeamLogoUrl,
                AwayLogoUrl = m.AwayTeamLogoUrl,
                m.KickoffTime,
                Status = MapStatus(m.StatusShort),
                m.HomeScore,
                m.AwayScore,
                Minute = m.Elapsed,
                IsHalfTime = m.StatusShort == "HT",
                HomeOdds = h2h?.FirstOrDefault(p => p.Side == "Home")?.Price,
                DrawOdds = h2h?.FirstOrDefault(p => p.Side == "Draw")?.Price,
                AwayOdds = h2h?.FirstOrDefault(p => p.Side == "Away")?.Price,
                Bookmaker = h2h?.FirstOrDefault()?.Bookmaker,
                BookmakerLogoUrl = (string?)null,
                BetBuilderAvailable = hasPropsFor.Contains(m.Id),
            };
        });

        return Ok(dtos);
    }

    [HttpGet("matches/{id:guid}/bet-builder-markets")]
    public async Task<IActionResult> BetBuilderMarkets(Guid id, CancellationToken ct)
    {
        var props = await db.PlayerPropMarkets
            .Where(p => p.MatchId == id)
            // Player-prop rows the surname-matcher couldn't place on either squad (see
            // SandboxPlayerPropMarket.Team) are dropped here rather than shown in their own
            // "Unmatched" group - match-level rows (PlayerName null) are unaffected.
            .Where(p => p.PlayerName == null || p.Team != null)
            // One row per (market, player, line, side) - prefer the first bookmaker seen rather
            // than showing every bookmaker's price separately, same simplification Highlightly's
            // real pipeline makes by standardizing on one bookmaker (see HighlightlyOptions.
            // BookmakerName) - good enough for testing how this renders, not a pricing decision.
            .GroupBy(p => new { p.MarketKey, p.PlayerName, p.Side, p.Point, p.PredictedHomeScore, p.PredictedAwayScore })
            .Select(g => g.OrderBy(p => p.FetchedAt).First())
            .ToListAsync(ct);

        var marketDtos = props.Select(p => new
        {
            MarketType = MapMarketKey(p.MarketKey),
            Line = p.Point,
            p.Side,
            p.PredictedHomeScore,
            p.PredictedAwayScore,
            p.Price,
            PlayerName = p.PlayerName,
            Team = p.Team,
        }).ToList();

        return Ok(new
        {
            Available = marketDtos.Count > 0,
            Markets = marketDtos,
            Bookmaker = props.Select(p => p.Bookmaker).FirstOrDefault(),
            BookmakerLogoUrl = (string?)null,
        });
    }

    // Client-side-only strings (see BetBuilder.razor's PlayerPropMarketTypes) - no shared enum with
    // the real FullTime.Api on either end of this mapping.
    private static string MapMarketKey(string marketKey) => marketKey switch
    {
        "player_goal_scorer_anytime" => "PlayerGoalscorerAnytime",
        "player_to_receive_card" => "PlayerCard",
        "player_shots_on_target" => "PlayerShotsOnTarget",
        "player_assists" => "PlayerAssists",
        "totals" or "alternate_totals" => "OverUnder",
        "btts" => "BothTeamsToScore",
        "alternate_totals_corners" => "TotalCorners",
        "correct_score" => "CorrectScore",
        _ => marketKey,
    };

    // API-Football short status codes -> the three buckets FullTime.App.Shared's UI actually
    // switches on (see MatchCard.razor). Confirmed live codes so far: NS, 1H, HT, 2H, FT - the rest
    // (ET/P/PEN/SUSP/INT/PST/CANC/ABD/AWD/WO) are API-Football's documented set, not yet seen live.
    private static string MapStatus(string statusShort) => statusShort switch
    {
        "NS" or "TBD" or "PST" => "Upcoming",
        "FT" or "AET" or "PEN" or "AWD" or "WO" or "CANC" or "ABD" => "Finished",
        _ => "InProgress",
    };
}
