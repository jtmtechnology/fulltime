namespace FullTime.Api.BetBuilder;

// Maps our own Match.LeagueId values (including the primary provider's temporary per-season IDs,
// same variants as FullTime.App.Shared's LeagueCatalog) to Highlightly's league IDs. Discovered via
// GET /football/leagues?leagueName=<exact name>&countryName=<country> — unlike odds-feed, every
// one of our competitions resolved to exactly one unambiguous result, no cross-checking needed.
//
// Two naming quirks: Highlightly calls the EFL Cup "League Cup", and the UEFA competitions need
// their full "UEFA ___" prefix (a bare "Champions League" search returns nothing — the name lookup
// appears to require an exact match, not a substring).
//
// The three UEFA cups' qualifying/play-off rounds share one pool of temp IDs on our side (see
// LeagueCatalog.UefaQualifyingIds) since our primary provider gives no reliable way to split them
// by competition. Highlightly keeps qualifying rounds under the *same* league ID as the main
// competition (confirmed via a real "2nd Qualifying Round" match under the Champions League's own
// ID), which is actually easier than odds-feed's separate-tournament-per-stage split — but since our
// own temp IDs still can't say which of the three cups a given qualifier belongs to, the sync still
// has to search all three and take whichever team+date match succeeds.
public static class HighlightlyLeagueMap
{
    private const int PremierLeague = 33973;
    private const int Championship = 34824;
    private const int LeagueOne = 35675;
    private const int LeagueTwo = 36526;
    private const int FaCup = 39079;
    private const int EflCup = 41632;
    private const int CommunityShield = 450112;
    private const int Bundesliga = 67162;
    private const int LaLiga = 119924;
    private const int Ligue1 = 52695;
    private const int SerieA = 115669;
    private const int ChampionsLeague = 2486;
    private const int EuropaLeague = 3337;
    private const int ConferenceLeague = 722432;

    public static readonly Dictionary<long, int[]> LeagueIds = new()
    {
        [47] = [PremierLeague],
        [48] = [Championship],
        [938218] = [Championship],
        [108] = [LeagueOne],
        [938219] = [LeagueOne],
        [109] = [LeagueTwo],
        [938220] = [LeagueTwo],
        [132] = [FaCup],
        [133] = [EflCup],
        [938221] = [EflCup],
        [247] = [CommunityShield],
        [54] = [Bundesliga],
        [87] = [LaLiga],
        [53] = [Ligue1],
        [55] = [SerieA],
        [42] = [ChampionsLeague],
        [10611] = [ChampionsLeague],
        [73] = [EuropaLeague],
        [10613] = [EuropaLeague],
        [10216] = [ConferenceLeague],
        [10615] = [ConferenceLeague],
        // Shared UEFA-qualifying pool (see LeagueCatalog.UefaQualifyingIds) — could be any of the three.
        [937348] = [ChampionsLeague, EuropaLeague, ConferenceLeague],
        [937349] = [ChampionsLeague, EuropaLeague, ConferenceLeague],
        [937351] = [ChampionsLeague, EuropaLeague, ConferenceLeague],
    };
}
