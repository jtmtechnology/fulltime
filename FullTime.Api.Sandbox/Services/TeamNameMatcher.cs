using System.Globalization;
using System.Text;

namespace FullTime.Api.Sandbox.Services;

// the-odds-api identifies matches by plain team-name strings only (no team ID, no shared key with
// any other provider - confirmed 2026-09-06, see HANDOVER.md). API-Football has its own numeric
// team/fixture IDs. Linking a the-odds-api event to one of our own matches means comparing team
// name strings, the exact fuzzy-matching problem the codebase deliberately eliminated when it
// consolidated onto Highlightly alone (see Match.cs's comment on the old provider). This class
// exists to find out whether that's actually reliable enough to reintroduce, or a real blocker.
public static class TeamNameMatcher
{
    // Generic suffixes/prefixes that one provider includes and the other may not (e.g. Highlightly
    // and API-Football both sometimes say "Manchester United FC", the-odds-api always just
    // "Manchester United") - stripped before comparing so their absence/presence doesn't tank the
    // score. Deliberately NOT stripping meaningful words like "United"/"City"/"Town" - those are
    // exactly what distinguishes Manchester United from Manchester City.
    private static readonly string[] NoiseTokens =
        ["fc", "afc", "cf", "sc", "ac", "calcio", "club", "the", "and"];

    public record MatchCandidate(string HomeTeam, string AwayTeam, DateTime KickoffTime);

    public record MatchResult<T>(T OddsApiEvent, T? BestMatch, double Score) where T : notnull;

    // Pairs each odds-api event against the best-scoring API-Football fixture on the same calendar
    // day (kickoff times from the two providers aren't guaranteed to agree to the minute - seen
    // both report the same match a few minutes apart during testing). Anything below
    // minScoreToAccept comes back with BestMatch = default and the raw score, so a caller can see
    // exactly how close the nearest miss was rather than just a bare "no match".
    public static List<(MatchCandidate OddsEvent, MatchCandidate? Match, double Score)> MatchAll(
        IEnumerable<MatchCandidate> oddsApiEvents,
        IEnumerable<MatchCandidate> apiFootballFixtures,
        double minScoreToAccept = 0.6)
    {
        var fixtures = apiFootballFixtures.ToList();
        var results = new List<(MatchCandidate, MatchCandidate?, double)>();

        foreach (var oddsEvent in oddsApiEvents)
        {
            var sameDay = fixtures.Where(f => f.KickoffTime.Date == oddsEvent.KickoffTime.Date).ToList();
            var candidates = sameDay.Count > 0 ? sameDay : fixtures;

            var scored = candidates
                .Select(f => (Fixture: f, Score: PairScore(oddsEvent, f)))
                .OrderByDescending(x => x.Score)
                .ToList();

            var best = scored.FirstOrDefault();
            results.Add(best.Score >= minScoreToAccept
                ? (oddsEvent, best.Fixture, best.Score)
                : (oddsEvent, null, best.Score));
        }

        return results;
    }

    private static double PairScore(MatchCandidate a, MatchCandidate b) =>
        (Similarity(a.HomeTeam, b.HomeTeam) + Similarity(a.AwayTeam, b.AwayTeam)) / 2.0;

    // Token-set (Jaccard) similarity after normalizing - robust to word-order and to one side
    // including extra noise words the other doesn't. Falls back to a character-level ratio when
    // either side normalizes to zero tokens (shouldn't happen for real team names, but cheap
    // insurance against a division-by-zero-shaped bug).
    public static double Similarity(string a, string b)
    {
        var tokensA = Normalize(a);
        var tokensB = Normalize(b);

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return CharacterRatio(a, b);
        }

        var setA = tokensA.ToHashSet();
        var setB = tokensB.ToHashSet();
        var intersection = setA.Intersect(setB).Count();
        var union = setA.Union(setB).Count();
        var jaccard = union == 0 ? 0 : (double)intersection / union;

        // A pure word-set score treats "Sheffield United" vs "Sheffield Wednesday" as 50% similar
        // (they share one of two tokens) when they're actually different clubs - blend in a
        // character-level ratio of the full normalized strings to catch that case.
        var charRatio = CharacterRatio(string.Join(' ', tokensA), string.Join(' ', tokensB));
        var blended = (jaccard * 0.6) + (charRatio * 0.4);

        // API-Football/Highlightly favor short colloquial names ("Brighton", "Coventry") where
        // the-odds-api uses full official ones ("Brighton and Hove Albion", "Coventry City") -
        // confirmed 2026-09-06 against real EPL fixtures, and it tanks the blended score above even
        // though it's an unambiguous match (every token of the shorter name appears in the longer
        // one). Treat a full subset as a strong signal on its own, but only when the shorter side
        // has at least one real token - an empty smaller set (already handled by the caller) or a
        // single very generic word shared by many clubs isn't in scope here since real team names
        // always carry at least one distinguishing token after noise-stripping.
        var smaller = setA.Count <= setB.Count ? setA : setB;
        var larger = setA.Count <= setB.Count ? setB : setA;
        var isFullSubset = smaller.Count > 0 && smaller.All(larger.Contains);

        return isFullSubset ? Math.Max(blended, 0.85) : blended;
    }

    private static List<string> Normalize(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        var ascii = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var cleaned = new string(ascii.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());

        return cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !NoiseTokens.Contains(t))
            .ToList();
    }

    // Levenshtein distance normalized to a 0-1 similarity ratio.
    private static double CharacterRatio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1.0;
        }

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 1.0 : 1.0 - ((double)distance / maxLen);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }
}
