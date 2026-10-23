using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashFlow.Application.Invitations;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Invitations;

// Serialize invitation mutations by business, then lock inviter membership/grant, then recipient.
// Membership-management writes must use the same business-first protocol.
public sealed class EfInvitationStore(WashFlowDbContext db, TimeProvider clock) : IInvitationStore
{
    private async Task LockBusiness(Guid id, CancellationToken ct) =>
        await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
    private async Task<BusinessMembership?> Owner(Guid user, Guid business, CancellationToken ct)
    {
        var membership = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {business} AND \"UserId\" = {user} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (membership is null || membership.RevokedAt is not null) return null;
        var grants = await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\" = {membership.Id} AND \"Role\" = 1 FOR UPDATE").AsNoTracking().ToListAsync(ct);
        return grants.Count == 1 ? membership : null;
    }
    private static InvitationView View(EmployeeInvitation i) => new(i.Id, i.NormalizedEmail, i.ExpiresAt, i.AcceptedAt, i.RevokedAt);
    private bool Pending(EmployeeInvitation i) => i.AcceptedAt is null && i.RevokedAt is null && i.CreatedAt <= clock.GetUtcNow() && i.ExpiresAt > clock.GetUtcNow();
    public async Task<InvitationResult> IssueAsync(Guid actor, Guid business, string? email, Guid? resendId, TokenHash hash, TimeSpan lifetime, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBusiness(business, ct);
        var owner = await Owner(actor, business, ct);
        if (owner is null) return new(InvitationStatus.Forbidden);
        EmployeeInvitation? previous = null;
        if (resendId is { } id)
        {
            previous = await db.EmployeeInvitations.SingleOrDefaultAsync(x => x.Id == id && x.BusinessId == business, ct);
            if (previous is null || previous.AcceptedAt is not null || previous.RevokedAt is not null) return new(InvitationStatus.Invalid);
            email = previous.NormalizedEmail;
        }
        if (email is null) return new(InvitationStatus.Invalid);
        if (await (from m in db.BusinessMemberships join u in db.Users on m.UserId equals u.Id
                   where m.BusinessId == business && m.RevokedAt == null && u.NormalizedEmail == email select m.Id).AnyAsync(ct))
            return new(InvitationStatus.Conflict);
        var outstanding = await db.EmployeeInvitations.Where(x => x.BusinessId == business && x.NormalizedEmail == email && x.AcceptedAt == null && x.RevokedAt == null).ToListAsync(ct);
        if (resendId is null && outstanding.Any(Pending)) return new(InvitationStatus.Conflict);
        var now = clock.GetUtcNow();
        foreach (var old in outstanding) old.RevokedAt = now;
        await db.SaveChangesAsync(ct);
        var invitation = new EmployeeInvitation { BusinessId = business, InvitedByMembershipId = previous?.InvitedByMembershipId ?? owner.Id,
            NormalizedEmail = email, TokenHash = hash, CreatedAt = now, ExpiresAt = now + lifetime };
        db.Add(invitation);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(InvitationStatus.Success, View(invitation));
    }
    public async Task<IReadOnlyList<InvitationView>?> ListAsync(Guid actor, Guid business, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBusiness(business, ct);
        if (await Owner(actor, business, ct) is null) return null;
        return await db.EmployeeInvitations.AsNoTracking().Where(x => x.BusinessId == business).OrderByDescending(x => x.CreatedAt)
            .Select(x => new InvitationView(x.Id, x.NormalizedEmail, x.ExpiresAt, x.AcceptedAt, x.RevokedAt)).ToListAsync(ct);
    }
    public async Task<InvitationStatus> RevokeAsync(Guid actor, Guid business, Guid invitation, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockBusiness(business, ct);
        if (await Owner(actor, business, ct) is null) return InvitationStatus.Forbidden;
        var row = await db.EmployeeInvitations.SingleOrDefaultAsync(x => x.Id == invitation && x.BusinessId == business, ct);
        if (row is null || row.AcceptedAt is not null) return InvitationStatus.Invalid;
        row.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return InvitationStatus.Success;
    }
    private async Task<EmployeeInvitation?> Resolve(TokenHash hash, CancellationToken ct)
    {
        var business = await db.EmployeeInvitations.AsNoTracking().Where(x => x.TokenHash == hash).Select(x => (Guid?)x.BusinessId).SingleOrDefaultAsync(ct);
        if (business is null) return null;
        await LockBusiness(business.Value, ct);
        var invitation = await db.EmployeeInvitations.SingleAsync(x => x.TokenHash == hash, ct);
        if (!Pending(invitation)) return null;
        var inviter = await db.BusinessMemberships.AsNoTracking().Where(x => x.Id == invitation.InvitedByMembershipId).Select(x => x.UserId).SingleAsync(ct);
        var owner = await Owner(inviter, business.Value, ct);
        return owner?.Id == invitation.InvitedByMembershipId && Pending(invitation) ? invitation : null;
    }
    public async Task<InvitationStatus> AcceptAsync(Guid actor, TokenHash hash, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invitation = await Resolve(hash, ct);
        if (invitation is null) return InvitationStatus.Invalid;
        var user = await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM \"Users\" WHERE \"Id\" = {actor} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        if (user?.EmailVerifiedAt is null || user.NormalizedEmail != invitation.NormalizedEmail ||
            invitation.ExpiresAt <= now || invitation.CreatedAt > now) return InvitationStatus.Invalid;
        // Reject historical memberships too until an explicit rehire policy exists. Never reactivate old grants.
        if (await db.BusinessMemberships.AnyAsync(x => x.BusinessId == invitation.BusinessId && x.UserId == actor, ct)) return InvitationStatus.Conflict;
        var membership = new BusinessMembership { BusinessId = invitation.BusinessId, UserId = actor, CreatedAt = clock.GetUtcNow() };
        db.AddRange(membership, new BusinessMembershipRole { BusinessMembershipId = membership.Id, Role = MembershipRole.Employee });
        invitation.AcceptedAt = now; invitation.AcceptedByUserId = actor;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return InvitationStatus.Success;
    }
    public async Task<bool> RegisterAsync(User user, EmailVerificationToken verification, TokenHash invitation, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = await Resolve(invitation, ct);
        if (row is null || row.NormalizedEmail != user.NormalizedEmail || await db.Users.AnyAsync(x => x.NormalizedEmail == user.NormalizedEmail, ct)) return false;
        db.AddRange(user, verification);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_NormalizedEmail" })
        { db.ChangeTracker.Clear(); return false; }
    }
}
