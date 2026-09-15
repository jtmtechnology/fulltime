namespace FullTime.Api.Models;

// Dedup ledger: one row per (user, match, alert type) that's actually been pushed, so a live-sync
// tick that notices "still InProgress, still 1-0" every 5 seconds - or a lineups-check tick that
// runs every 5 minutes right up to kickoff - never re-sends the same alert. See
// MatchAlertService.NotifyAsync, the only place that reads or writes this table. Nothing like this
// existed before this feature - the closest analogues (User.LastSpinReminderDate,
// ApiFootballMatchSyncService's in-memory stale-match-alert dictionary) are both single-purpose,
// once-a-day shapes that don't fit "up to six distinct alert types per match".
public class SentMatchAlert
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public User? User { get; set; }
    public required Guid MatchId { get; set; }
    public Match? Match { get; set; }
    public MatchAlertType AlertType { get; set; }

    // Kickoff/HalfTime/FullTime/LineupsOut only ever happen once per match, so 0 is enough to dedup
    // them. Goal, RedCard and YellowCard can happen repeatedly in the same match, so the
    // (user, match, type) triple alone isn't a fine enough key - it would let the very first goal's
    // dedup row silently block every later goal in the same match too. Goal uses the running total
    // goal count at the moment it's detected (HomeScore+AwayScore, always increasing, so always a
    // fresh value per goal); RedCard/YellowCard use the event's own minute (two cards of the same
    // type in the exact same minute would collide and the second would be silently skipped - an
    // accepted, vanishingly rare edge case rather than tracking a separate per-event ID API-Football
    // doesn't provide anyway).
    public int Sequence { get; set; }

    public DateTime SentAt { get; set; }
}
