namespace FullTime.Api.Models;

public enum MatchStatus
{
    Upcoming,
    Finished,
    InProgress,

    // Highlightly reported this fixture's state.description as "Postponed" - kept distinct from
    // Upcoming so it stops being treated as "hasn't kicked off yet" (see
    // HighlightlyMatchSyncService.RefreshLiveAsync's staleKickoffs query, which used to re-add a
    // postponed match's original kickoff date to every single live-sync tick forever, since it
    // never reaches Finished - confirmed in production 2026-09-09: this alone doubled the
    // Highlightly call count on every tick and was a real contributor to that day's quota
    // exhaustion). Also excluded from the app's match listings entirely, same as the user's request.
    Postponed,

    // Terminal: API-Football ABD/CANC, or INT/SUSP that never resumed. Its bet legs are voided
    // rather than settled on whatever partial score was last seen, as a bookmaker would (Rayo
    // Vallecano v Athletic Club, 2026-10-10, sat at INT for a day with no further update).
    Abandoned
}
