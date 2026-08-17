namespace FullTime.Api.Models;

public class League
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string InviteCode { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<LeagueMembership> Memberships { get; set; } = [];
}
