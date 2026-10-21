using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Authentication;

public sealed class EfAuthStore(WashFlowDbContext db) : IAuthStore
{
    public async Task RegisterAsync(User user, string? businessName, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == user.NormalizedEmail, ct)) return;
        db.Users.Add(user);
        if (businessName is null) db.CustomerProfiles.Add(new() { UserId = user.Id });
        else
        {
            var business = new Business { Name = businessName };
            var membership = new BusinessMembership { BusinessId = business.Id, UserId = user.Id };
            db.AddRange(business, membership, new BusinessMembershipRole
                { BusinessMembershipId = membership.Id, Role = MembershipRole.Owner });
        }
        // A single SaveChanges transaction commits all records or none, including duplicate-email races.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_NormalizedEmail" })
        { db.ChangeTracker.Clear(); }
    }

    public Task<User?> FindUserAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct);

    public async Task<DeviceSession?> StartSessionAsync(User user, string? upgradedHash, string deviceName,
        TokenHash hash, DateTimeOffset now, CancellationToken ct)
    {
        // PostgreSQL timestamps have microsecond precision; return the same expiry after a reload/rotation.
        now = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        var current = await db.Users.SingleOrDefaultAsync(x => x.Id == user.Id, ct);
        if (current?.EmailVerifiedAt is null || current.SecurityStamp != user.SecurityStamp) return null;
        if (upgradedHash is not null) current.UpgradePasswordHash(upgradedHash);
        var session = new DeviceSession { UserId = user.Id, SecurityStamp = current.SecurityStamp,
            DeviceName = deviceName, CreatedAt = now, ExpiresAt = now.AddDays(30) };
        db.AddRange(session, new RefreshToken { DeviceSessionId = session.Id, TokenHash = hash, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return null; }
        return session;
    }

    public async Task<DeviceSession?> RotateAsync(TokenHash oldHash, TokenHash newHash, DateTimeOffset now, CancellationToken ct)
    {
        var sessionId = await db.RefreshTokens.AsNoTracking().Where(x => x.TokenHash == oldHash)
            .Select(x => (Guid?)x.DeviceSessionId).SingleOrDefaultAsync(ct);
        if (sessionId is null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var session = await LockSession(sessionId.Value, ct);
        if (session is null) return null;
        var token = await db.RefreshTokens.SingleAsync(x => x.TokenHash == oldHash, ct);
        if (token.ConsumedAt is not null || token.RevokedAt is not null)
        {
            await Revoke(session, now, ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        if (!await IsSessionActiveAsync(session.UserId, session.Id, now, ct)) return null;
        token.ConsumedAt = now;
        await db.SaveChangesAsync(ct); // Release the partial unique index before inserting the successor.
        var replacement = new RefreshToken { DeviceSessionId = session.Id, TokenHash = newHash, CreatedAt = now };
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        token.ReplacedByTokenId = replacement.Id;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var session = await LockSession(sessionId, ct);
        if (session is not null && session.UserId == userId) await Revoke(session, now, ct);
        await transaction.CommitAsync(ct);
    }

    public Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct) =>
        (from session in db.DeviceSessions.AsNoTracking()
         join user in db.Users.AsNoTracking() on session.UserId equals user.Id
         where session.Id == sessionId && user.Id == userId && session.RevokedAt == null &&
             session.ExpiresAt > now && user.EmailVerifiedAt != null && session.SecurityStamp == user.SecurityStamp
         select session.Id).AnyAsync(ct);

    private Task<DeviceSession?> LockSession(Guid id, CancellationToken ct) =>
        db.DeviceSessions.FromSqlInterpolated($"SELECT *, xmin FROM \"DeviceSessions\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private async Task Revoke(DeviceSession session, DateTimeOffset now, CancellationToken ct)
    {
        session.RevokedAt ??= now;
        await db.SaveChangesAsync(ct);
        await db.RefreshTokens.Where(x => x.DeviceSessionId == session.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.UpdatedAt, now), ct);
    }
}
