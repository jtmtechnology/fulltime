namespace FullTime.Api.BetBuilder.ApiFootball;

// API-Football's own league IDs — looked up via its own GET /leagues?name=&country= endpoint
// against the real API on 2026-09-06 (not guessed), one exact-name query per competition. UEFA
// club competitions have no single country, so those were looked up by name alone.
public static class ApiFootballLeagueMap
{
    public const int PremierLeague = 39;
    public const int Championship = 40;
    public const int LeagueOne = 41;
    public const int LeagueTwo = 42;
    public const int FaCup = 45;
    public const int EflCup = 48;
    public const int CommunityShield = 528;
    public const int Bundesliga = 78;
    public const int LaLiga = 140;
    public const int Ligue1 = 61;
    public const int SerieA = 135;
    public const int ChampionsLeague = 2;
    public const int EuropaLeague = 3;
    public const int ConferenceLeague = 848;

    public static readonly HashSet<long> TrackedLeagueIds = new()
    {
        PremierLeague,
        Championship,
        LeagueOne,
        LeagueTwo,
        FaCup,
        EflCup,
        CommunityShield,
        Bundesliga,
        LaLiga,
        Ligue1,
        SerieA,
        ChampionsLeague,
        EuropaLeague,
        ConferenceLeague,
    };
}
