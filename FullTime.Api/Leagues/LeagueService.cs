using System.Net;
using FullTime.Api.Auth;
using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Moderation;
using FullTime.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Leagues;

public class LeagueService(
    AppDbContext db, PushNotificationService push, IEmailSender emailSender, IOptions<BettingOptions> options)
{
    // Avoids 0/O and 1/I, which look alike when a code is read aloud or typed from a text message.
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    // The marketing site (FullTime.Website, separate app/domain from the API and the Blazor app).
    private const string WebsiteUrl = "https://fulltime.jtmtechnology.co.uk";

    // Keeps the My Leagues list and the worldwide per-membership leaderboard from growing unbounded
    // for one person - 5 is generous for a casual friends-and-family app while still being a real cap.
    public const int MaxLeaguesPerUser = 5;

    public async Task<CreateLeagueResult> CreateAsync(Guid userId, string name, CancellationToken ct = default)
    {
        if (ProfanityFilter.ContainsProfanity(name))
        {
            return new CreateLeagueResult(CreateLeagueOutcome.ProfaneName);
        }

        var membershipCount = await db.LeagueMemberships.CountAsync(m => m.UserId == userId, ct);
        if (membershipCount >= MaxLeaguesPerUser)
        {
            return new CreateLeagueResult(CreateLeagueOutcome.MaxLeaguesReached);
        }

        // Case-insensitive - otherwise "The Brownes" and "the brownes" would both show up on the
        // worldwide leaderboard's League column looking like the same league, which is exactly the
        // confusion this check exists to avoid.
        var nameTaken = await db.Leagues.AnyAsync(l => l.Name.ToLower() == name.ToLower(), ct);
        if (nameTaken)
        {
            return new CreateLeagueResult(CreateLeagueOutcome.NameTaken);
        }

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
            StartingBalance = options.Value.StartingBalance,
        });

        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct);

        return new CreateLeagueResult(CreateLeagueOutcome.Success, league);
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

        var membershipCount = await db.LeagueMemberships.CountAsync(m => m.UserId == userId, ct);
        if (membershipCount >= MaxLeaguesPerUser)
        {
            return new JoinLeagueResult(JoinLeagueOutcome.MaxLeaguesReached);
        }

        db.LeagueMemberships.Add(new LeagueMembership
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            Balance = options.Value.StartingBalance,
            StartingBalance = options.Value.StartingBalance,
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

    public async Task<InviteOutcome> SendInviteAsync(Guid leagueId, Guid requestingUserId, string email, CancellationToken ct = default)
    {
        var league = await db.Leagues.FindAsync([leagueId], ct);
        if (league is null)
        {
            return InviteOutcome.LeagueNotFound;
        }

        var isMember = await db.LeagueMemberships.AnyAsync(m => m.LeagueId == leagueId && m.UserId == requestingUserId, ct);
        if (!isMember)
        {
            return InviteOutcome.NotMember;
        }

        var inviterName = await db.Users.Where(u => u.Id == requestingUserId).Select(u => u.Name).SingleAsync(ct);
        var subject = $"{inviterName} invited you to join {league.Name} on FullTime";
        var inviteLink = $"{WebsiteUrl}/invite.html?code={Uri.EscapeDataString(league.InviteCode)}&league={Uri.EscapeDataString(league.Name)}";

        var html = $"""
            <div style="font-family: -apple-system, Segoe UI, Roboto, Arial, sans-serif; background: #0D1117; padding: 32px 16px;">
              <div style="max-width: 480px; margin: 0 auto; background: #161B22; border-radius: 16px; padding: 32px 24px; text-align: center;">
                <img src="{WebsiteUrl}/logo.png" alt="FullTime" width="56" height="56" style="margin-bottom: 16px;" />
                <h1 style="color: #ffffff; font-size: 20px; margin: 0 0 8px;">{WebUtility.HtmlEncode(inviterName)} wants you on their team</h1>
                <p style="color: #9CA3AF; font-size: 15px; margin: 0 0 24px;">
                  You've been invited to join <strong style="color: #ffffff;">{WebUtility.HtmlEncode(league.Name)}</strong>
                  on FullTime — no real money, just bragging rights.
                </p>
                <div style="background: #0D1117; border: 1px dashed #2FAE4F; border-radius: 10px; padding: 12px; margin: 0 0 24px;">
                  <div style="color: #9CA3AF; font-size: 12px; text-transform: uppercase; letter-spacing: 0.05em;">Invite code</div>
                  <div style="color: #2FAE4F; font-size: 24px; font-weight: 700; letter-spacing: 0.1em;">{WebUtility.HtmlEncode(league.InviteCode)}</div>
                </div>
                <a href="{inviteLink}" style="display: inline-block; background: #2FAE4F; color: #0D1117; font-weight: 700; text-decoration: none; padding: 12px 28px; border-radius: 8px; font-size: 15px;">
                  Join {WebUtility.HtmlEncode(league.Name)}
                </a>
              </div>
            </div>
            """;

        var text = $"{inviterName} wants you to join their FullTime league \"{league.Name}\"!\n\n" +
            $"Invite code: {league.InviteCode}\n" +
            $"Join here: {inviteLink}";

        await emailSender.SendHtmlAsync(email, subject, html, text, ct);

        return InviteOutcome.Success;
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
