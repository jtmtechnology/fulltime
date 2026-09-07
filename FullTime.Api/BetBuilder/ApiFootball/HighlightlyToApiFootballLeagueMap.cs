namespace FullTime.Api.BetBuilder.ApiFootball;

// Bridges Highlightly's league IDs (HighlightlyLeagueMap, what Match.LeagueId actually holds while
// Highlightly is the live-score provider) to API-Football's own league IDs (ApiFootballLeagueMap) -
// both maps already track the same 14 competitions by name, this just connects them. Needed by
// ApiFootballSettlementSupportService.ResolvePlayerStatsAsync to know which API-Football league to
// search when looking up the real fixture for a Highlightly-sourced match (Match.ExternalId is
// Highlightly's own match ID, not API-Football's fixture ID - see the 2026-09-07 investigation that
// found this the same way for team squads, see ApiFootballEplTeamMap).
public static class HighlightlyToApiFootballLeagueMap
{
    public static readonly Dictionary<long, long> LeagueIds = new()
    {
        [HighlightlyLeagueMap.PremierLeague] = ApiFootballLeagueMap.PremierLeague,
        [HighlightlyLeagueMap.Championship] = ApiFootballLeagueMap.Championship,
        [HighlightlyLeagueMap.LeagueOne] = ApiFootballLeagueMap.LeagueOne,
        [HighlightlyLeagueMap.LeagueTwo] = ApiFootballLeagueMap.LeagueTwo,
        [HighlightlyLeagueMap.FaCup] = ApiFootballLeagueMap.FaCup,
        [HighlightlyLeagueMap.EflCup] = ApiFootballLeagueMap.EflCup,
        [HighlightlyLeagueMap.CommunityShield] = ApiFootballLeagueMap.CommunityShield,
        [HighlightlyLeagueMap.Bundesliga] = ApiFootballLeagueMap.Bundesliga,
        [HighlightlyLeagueMap.LaLiga] = ApiFootballLeagueMap.LaLiga,
        [HighlightlyLeagueMap.Ligue1] = ApiFootballLeagueMap.Ligue1,
        [HighlightlyLeagueMap.SerieA] = ApiFootballLeagueMap.SerieA,
        [HighlightlyLeagueMap.ChampionsLeague] = ApiFootballLeagueMap.ChampionsLeague,
        [HighlightlyLeagueMap.EuropaLeague] = ApiFootballLeagueMap.EuropaLeague,
        [HighlightlyLeagueMap.ConferenceLeague] = ApiFootballLeagueMap.ConferenceLeague,
    };
}
