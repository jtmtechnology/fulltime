namespace FullTime.Api.BetBuilder;

// The Highlightly league IDs we track. Discovered via GET /football/leagues?leagueName=<exact
// name>&countryName=<country> — every one of our competitions resolved to exactly one unambiguous
// result. Highlightly keeps qualifying/play-off rounds under the *same* league ID as the main
// competition (confirmed via a real "2nd Qualifying Round" match under the Champions League's own
// ID), so unlike the old provider there's no separate qualifying-pool ID to track.
public static class HighlightlyLeagueMap
{
    public const int PremierLeague = 33973;
    public const int Championship = 34824;
    public const int LeagueOne = 35675;
    public const int LeagueTwo = 36526;
    public const int FaCup = 39079;
    public const int EflCup = 41632;
    public const int CommunityShield = 450112;
    public const int Bundesliga = 67162;
    public const int LaLiga = 119924;
    public const int Ligue1 = 52695;
    public const int SerieA = 115669;
    public const int ChampionsLeague = 2486;
    public const int EuropaLeague = 3337;
    public const int ConferenceLeague = 722432;

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
