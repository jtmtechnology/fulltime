using System.Globalization;
using System.Text;

namespace FullTime.Api.BetBuilder.OddsApi;

// the-odds-api identifies matches by plain team-name strings only (no team ID, no shared key with
// API-Football). Ported verbatim from FullTime.Api.Sandbox/Services/TeamNameMatcher.cs, which
// validated this at 11/11 (100%) against real Premier League fixtures during evaluation — do not
// rewrite, per the accepted cutover plan.
public static class TeamNameMatcher
{
    private static readonly string[] NoiseTokens =
        ["fc", "afc", "cf", "sc", "ac", "calcio", "club", "the", "and"];

    // API-Football abbreviates "United" as "Utd" for some clubs (confirmed real: "Sheffield Utd"),
    // where the-odds-api always spells it out ("Sheffield United") - unlike a noise token, this
    // can't just be stripped (it's the distinguishing word for that club), it has to be normalized
    // to the same canonical spelling both sides use, or the token-set/Jaccard component scores two
    // real matches as barely related. Confirmed missing during the 2026-09-06 cutover: Blackburn v
    // Sheffield Utd never linked to the real the-odds-api event for it despite an exact kickoff-time
    // match, purely because of this spelling difference.
    // "st" -> "stanley" is intentionally narrow (Accrington Stanley is the only "___ Stanley" club
    // in any tracked league, confirmed real 2026-09-06: API-Football's "Accrington ST" never linked
    // to the-odds-api's "Accrington Stanley") - a 2-letter token synonym is too easy to false-match
    // elsewhere (e.g. "St" as an abbreviation for "Saint"/"Street"), so this only exists because no
    // tracked league currently has that competing case.
    // "brom" -> "bromwich": API-Football's own name for the club is literally "West Brom" (confirmed
    // via its /teams search 2026-09-06), where the-odds-api always spells out "West Bromwich
    // Albion" - same reasoning as "utd" above, "brom" is the distinguishing word, can't be stripped.
    private static readonly Dictionary<string, string> TokenSynonyms = new()
    {
        ["utd"] = "united",
        ["brom"] = "bromwich",
        ["st"] = "stanley",
    };

    // Whole-name acronym expansions, checked before tokenizing (unlike TokenSynonyms, these acronyms
    // have no spaces of their own to split on) - confirmed real 2026-09-06: API-Football's "QPR"
    // never linked to the-odds-api's "Queens Park Rangers" (zero shared tokens, nothing for the
    // Jaccard/subset logic to work with at all).
    private static readonly Dictionary<string, string> WholeNameSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["qpr"] = "queens park rangers",
    };

    public record MatchCandidate(string HomeTeam, string AwayTeam, DateTime KickoffTime);

    public static (T OddsEvent, double Score)? FindBest<T>(
        IEnumerable<(T Event, MatchCandidate Candidate)> oddsApiEvents,
        MatchCandidate ourMatch,
        double minScoreToAccept = 0.6)
    {
        var scored = oddsApiEvents
            .Select(e => (e.Event, Score: PairScore(e.Candidate, ourMatch)))
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored.FirstOrDefault();
        return best.Score >= minScoreToAccept ? (best.Event, best.Score) : null;
    }

    private static double PairScore(MatchCandidate a, MatchCandidate b) =>
        (Similarity(a.HomeTeam, b.HomeTeam) + Similarity(a.AwayTeam, b.AwayTeam)) / 2.0;

    // Token-set (Jaccard) similarity after normalizing — robust to word-order and to one side
    // including extra noise words the other doesn't. Falls back to a character-level ratio when
    // either side normalizes to zero tokens.
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

        var charRatio = CharacterRatio(string.Join(' ', tokensA), string.Join(' ', tokensB));
        var blended = (jaccard * 0.6) + (charRatio * 0.4);

        // API-Football favors short colloquial names ("Brighton") where the-odds-api uses full
        // official ones ("Brighton and Hove Albion") — treat a full subset as a strong signal on
        // its own, since every token of the shorter name appears in the longer one.
        var smaller = setA.Count <= setB.Count ? setA : setB;
        var larger = setA.Count <= setB.Count ? setB : setA;
        var isFullSubset = smaller.Count > 0 && smaller.All(larger.Contains);

        return isFullSubset ? Math.Max(blended, 0.85) : blended;
    }

    private static List<string> Normalize(string name)
    {
        if (WholeNameSynonyms.TryGetValue(name.Trim(), out var expanded))
        {
            name = expanded;
        }

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
            // Purely-numeric tokens ("1899 Hoffenheim" vs the-odds-api's "TSG Hoffenheim", "Bayer 04
            // Leverkusen" vs "Bayer Leverkusen") - confirmed real 2026-09-06: several German clubs'
            // year/number suffixes are included inconsistently between the two providers. A bare
            // number essentially never distinguishes one real fixture from another on the same day
            // in the same competition, so it's dropped the same way NoiseTokens are rather than
            // scored as a mismatch.
            .Where(t => !t.All(char.IsDigit))
            .Where(t => !NoiseTokens.Contains(t))
            .Select(t => TokenSynonyms.GetValueOrDefault(t, t))
            .ToList();
    }

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
