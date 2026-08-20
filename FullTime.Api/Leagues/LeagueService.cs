using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Leagues;

public class LeagueService(AppDbContext db, PushNotificationService push, IOptions<BettingOptions> options)
{
    // Avoids 0/O and 1/I, which look alike when a code is read aloud or typed from a text message.
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    public async Task<League> CreateAsync(Guid userId, string name, CancellationToken ct = default)
    {
        var league = new League
        {
            Id = Guid.NewGuid(),
            Name = name,
            InviteCode = await GenerateUniqueCodeAsync(ct),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        league.Memberships.Add(new LeagueMembership
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            UserId = userId,
            JoinedAt = league.CreatedAt,
            Balance = options.Value.StartingBalance,
        });

        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct);

        return league;
    }

    public async Task<JoinLeagueResult> JoinAsync(Guid userId, string inviteCode, CancellationToken ct = default)
    {
        var normalized = inviteCode.Trim().ToUpperInvariant();
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.InviteCode == normalized, ct);
        if (league is null)
        {
            return new JoinLeagueResult(JoinLeagueOutcome.InvalidCode);
        }

        var alreadyMember = await db.LeagueMemberships
            .AnyAsync(m => m.LeagueId == league.Id && m.UserId == userId, ct);
        if (alreadyMember)
        {
            return new JoinLeagueResult(JoinLeagueOutcome.AlreadyMember);
        }

        db.LeagueMemberships.Add(new LeagueMembership
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            Balance = options.Value.StartingBalance,
        });
        await db.SaveChangesAsync(ct);

        var otherMemberIds = await db.LeagueMemberships
            .Where(m => m.LeagueId == league.Id && m.UserId != userId)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        var joiningUserName = await db.Users.Where(u => u.Id == userId).Select(u => u.Name).SingleAsync(ct);
        await push.SendToUsersAsync(otherMemberIds, "New league member", $"{joiningUserName} joined {league.Name}", ct);

        return new JoinLeagueResult(JoinLeagueOutcome.Success, league);
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GenerateCode();
            if (!await db.Leagues.AnyAsync(l => l.InviteCode == code, ct))
            {
                return code;
            }
        }

        // 32^6 (~1.07 billion) combinations at friend-group scale — this is not reachable in
        // practice, just a safety net so a pathological run of collisions fails loudly (500) rather
        // than looping forever.
        throw new InvalidOperationException("Could not generate a unique invite code after 5 attempts.");
    }

    private static string GenerateCode() =>
        new(Enumerable.Range(0, CodeLength).Select(_ => CodeChars[Random.Shared.Next(CodeChars.Length)]).ToArray());
}
