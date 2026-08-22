using Microsoft.AspNetCore.SignalR;

namespace FullTime.Api.Realtime;

// Broadcast-only shape for a match's score/status right now — deliberately not the full match DTO,
// since clients already have everything else (teams, kickoff, odds) from their last API fetch and
// only need enough to patch a displayed match card in place.
public record MatchLiveUpdate(Guid MatchId, int? HomeScore, int? AwayScore, string Status);

// No client-callable methods — this hub only pushes. HighlightlyMatchSyncService broadcasts to it
// (via IHubContext<MatchUpdatesHub>) whenever a tracked match's score or status actually changes,
// so connected clients (Matches.razor, via MatchUpdatesClient) see it immediately instead of
// waiting for their own next poll.
public class MatchUpdatesHub : Hub;
