namespace FullTime.Api.BetBuilder.ApiFootball;

// Maps the-odds-api's own EPL team-name strings (as returned by its /events endpoint - confirmed
// against the real 2026-09-07 fixture list) to API-Football's team IDs, so AnytimeGoalscorerService
// can resolve a squad (GetSquadPlayerNamesAsync) without needing Match.HomeTeamId/AwayTeamId, which
// are Highlightly's own team-ID space, not API-Football's.
//
// Resolved via API-Football's real /teams?search= endpoint, one name at a time, confirmed
// individually (never guessed) - see ODDS_API_PLAYER_PROPS_INVESTIGATION.md. Several clubs only
// returned reserve/U21/women's entries when searched by their full odds-api name (e.g. "Newcastle
// United" only matched "Newcastle United U21"/"...W") - the senior first team is indexed under the
// shorter colloquial name instead ("Newcastle", "Leeds", "Tottenham", "Coventry", "Ipswich"), a real
// API-Football quirk confirmed for exactly these five clubs.
//
// This is a fixed list for the current EPL season only - needs a manual update after each
// promotion/relegation cycle (whichever three clubs go down, whichever three come up).
public static class ApiFootballEplTeamMap
{
    public static readonly Dictionary<string, long> TeamIds = new()
    {
        ["Aston Villa"] = 66,
        ["Nottingham Forest"] = 65,
        ["Bournemouth"] = 35,
        ["Brentford"] = 55,
        ["Chelsea"] = 49,
        ["Hull City"] = 64,
        ["Crystal Palace"] = 52,
        ["Ipswich Town"] = 57,
        ["Liverpool"] = 40,
        ["Fulham"] = 36,
        ["Tottenham Hotspur"] = 47,
        ["Everton"] = 45,
        ["Sunderland"] = 746,
        ["Arsenal"] = 42,
        ["Coventry City"] = 1346,
        ["Brighton and Hove Albion"] = 51,
        ["Manchester United"] = 33,
        ["Manchester City"] = 50,
        ["Leeds United"] = 63,
        ["Newcastle United"] = 34,
    };
}
