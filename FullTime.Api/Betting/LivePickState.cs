using FullTime.Api.Models;

namespace FullTime.Api.Betting;

// Display-only "how is this pick doing right now" for My Bets while its match is live. Never
// written anywhere - SettlementService still decides every outcome at full time (early payouts
// aside). Delegates to SettlementService.IsPickCorrect wherever it can, so a pick can't show as
// landed live and then settle the other way on the same score/events.
//
// Landed / Dead: can't change any more short of VAR (score-based ones fix themselves on the next
// tick when a goal is chalked off). Winning / Losing: would win / lose if the match ended now.
public static class LivePickState
{
    public const string Landed = "Landed";
    public const string Dead = "Dead";
    public const string Winning = "Winning";
    public const string Losing = "Losing";

    private static readonly MarketType[] OneWayUp =
    [
        MarketType.OverUnder, MarketType.BothTeamsToScore, MarketType.PlayerGoalscorerAnytime,
        MarketType.PlayerCard, MarketType.PlayerRedCard, MarketType.PlayerAssists,
    ];

    // Expects match.Events loaded for event-based markets.
    public static string? For(BetLegPick pick, Match match)
    {
        if (pick.Outcome != SelectionOutcome.Pending || match.Status != MatchStatus.InProgress
            || match.HomeScore is not { } home || match.AwayScore is not { } away)
        {
            return null;
        }

        switch (pick.MarketType)
        {
            case MarketType.MatchResult:
            {
                var leading = home > away ? SelectionSide.Home : home < away ? SelectionSide.Away : SelectionSide.Draw;
                return pick.Side == leading ? Winning : Losing;
            }
            case MarketType.CorrectScore:
                if (home > pick.PredictedHomeScore || away > pick.PredictedAwayScore)
                {
                    return Dead;
                }
                return home == pick.PredictedHomeScore && away == pick.PredictedAwayScore ? Winning : Losing;
            case MarketType.FirstTeamToScore:
                return FirstTeamToScore(pick, match, home, away);
            case var market when OneWayUp.Contains(market):
                // Every one of these counts something that only goes up during a match (goals,
                // cards, assists), so a Yes/Over that's true now stays true, and a No/Under that's
                // false now stays false.
                var correctNow = SettlementService.IsPickCorrect(pick, match);
                return pick.Side is SelectionSide.Yes or SelectionSide.Over
                    ? correctNow ? Landed : Losing
                    : correctNow ? Winning : Dead;
            default:
                // Corners, shots, fouls and team cards settle off post-match stats - nothing live.
                return null;
        }
    }

    private static string? FirstTeamToScore(BetLegPick pick, Match match, int home, int away)
    {
        if (home + away == 0)
        {
            return pick.Side == SelectionSide.None ? Winning : Losing;
        }

        // With one goal the score says who got it. Past that it needs the event feed, which can lag
        // the score by a poll or two - better to show nothing than guess.
        var firstSide = home + away == 1
            ? home == 1 ? SelectionSide.Home : SelectionSide.Away
            : match.Events
                .Where(e => e.Type is "Goal" or "Penalty" or "Own Goal")
                .OrderBy(e => MinuteSortKey(e.Minute))
                .Select(e => (SelectionSide?)e.Team)
                .FirstOrDefault();

        return firstSide is null ? null : pick.Side == firstSide ? Landed : Dead;
    }

    // "45+2" sorts after "45" and before "46".
    private static double MinuteSortKey(string minute)
    {
        var parts = minute.Split('+');
        var baseMinute = int.TryParse(parts[0], out var b) ? b : 0;
        var added = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : 0;
        return baseMinute + added / 100.0;
    }
}
