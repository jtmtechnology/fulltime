using System.Text.RegularExpressions;

namespace FullTime.Api.Leagues;

// Whole-word matching (not raw substring) to avoid the classic "Scunthorpe problem" - a naive
// Contains check would false-positive on names like "Scunthorpe" (contains "cunt") or "assassin"/
// "assessment" (start with "ass"). Common inflected forms (fucking, bitches, etc.) are listed
// explicitly rather than suffix-matched, since suffix matching re-introduces the same false-positive
// risk. This is a plain blocklist for a casual family app's league-naming field, not a general
// content-moderation system - it won't catch creative evasions (f*ck, deliberate misspellings).
public static class ProfanityFilter
{
    private static readonly string[] BlockedWords =
    [
        "fuck", "fucking", "fucker", "fucked", "motherfucker",
        "shit", "shitty", "bullshit",
        "bitch", "bitches",
        "cunt", "asshole", "bastard", "dick", "dickhead", "piss", "pissed",
        "wank", "wanker", "bollocks", "twat", "slut", "whore", "cock", "pussy",
        "nigger", "faggot", "retard",
    ];

    private static readonly Regex Pattern = new(
        $@"\b({string.Join("|", BlockedWords)})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContainsProfanity(string text) => Pattern.IsMatch(text);
}
