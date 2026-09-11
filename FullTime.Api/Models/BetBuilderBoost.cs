namespace FullTime.Api.Models;

// One row per calendar date (server-local, same convention as User.LastSpinDate) - the single
// match every user sees boosted that day, picked at random the first time anyone asks
// (BetBuilderBoostService.GetStatusAsync) from that day's Upcoming Premier League/Championship/
// League One/League Two fixtures with Bet Builder odds already available. Never re-picked once
// set, even if asked again later the same day.
public class BetBuilderBoost
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }
}
