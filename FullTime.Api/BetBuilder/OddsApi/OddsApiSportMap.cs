using FullTime.Api.BetBuilder.ApiFootball;

namespace FullTime.Api.BetBuilder.OddsApi;

// Maps API-Football's league IDs (Match.LeagueId, once the live-score cutover is active) to
// the-odds-api's own sport keys — confirmed against the real GET /v4/sports/ listing. There is no
// shared ID between the two providers at all, hence a hand-built map rather than a formula.
// Community Shield has no entry: the-odds-api doesn't cover it (a single one-off exhibition match,
// not a season-long competition) — matches in that league simply never get markets/odds, only live
// scores from API-Football.
public static class OddsApiSportMap
{
    private static readonly Dictionary<long, string> SportKeyByLeagueId = new()
    {
        [ApiFootballLeagueMap.PremierLeague] = "soccer_epl",
        [ApiFootballLeagueMap.Championship] = "soccer_efl_champ",
        [ApiFootballLeagueMap.LeagueOne] = "soccer_england_league1",
        [ApiFootballLeagueMap.LeagueTwo] = "soccer_england_league2",
        [ApiFootballLeagueMap.FaCup] = "soccer_fa_cup",
        [ApiFootballLeagueMap.EflCup] = "soccer_england_efl_cup",
        [ApiFootballLeagueMap.Bundesliga] = "soccer_germany_bundesliga",
        [ApiFootballLeagueMap.LaLiga] = "soccer_spain_la_liga",
        [ApiFootballLeagueMap.Ligue1] = "soccer_france_ligue_one",
        [ApiFootballLeagueMap.SerieA] = "soccer_italy_serie_a",
        [ApiFootballLeagueMap.ChampionsLeague] = "soccer_uefa_champs_league",
        [ApiFootballLeagueMap.EuropaLeague] = "soccer_uefa_europa_league",
        [ApiFootballLeagueMap.ConferenceLeague] = "soccer_uefa_europa_conference_league",
    };

    public static string? SportKeyFor(long leagueId) => SportKeyByLeagueId.GetValueOrDefault(leagueId);
}
