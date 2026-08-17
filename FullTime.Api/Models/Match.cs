namespace FullTime.Api.Models;

public class Match
{
    public Guid Id { get; set; }
    public required string ExternalId { get; set; }
    public long LeagueId { get; set; }
    public required string HomeTeam { get; set; }
    public required string AwayTeam { get; set; }
    public long HomeTeamId { get; set; }
    public long AwayTeamId { get; set; }
    public DateTime KickoffTime { get; set; }
    public MatchStatus Status { get; set; }
    public MatchOutcome? Result { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public List<OddsSnapshot> OddsSnapshots { get; set; } = [];
    public List<BetSelection> BetSelections { get; set; } = [];
}
