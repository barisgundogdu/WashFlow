using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Authentication;

public sealed class EfEmailVerificationStore(WashFlowDbContext db, TimeProvider clock) : IEmailVerificationStore
{
    public async Task<VerificationRecipient?> ReplaceAsync(string normalizedEmail, TokenHash hash, DateTimeOffset now, VerificationOptions options, CancellationToken ct)
    {
        var id = await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == normalizedEmail).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (id is null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(id.Value, ct);
        // Re-read the clock after waiting for the lock; a queued request cannot redeem an expired token.
        now = clock.GetUtcNow();
        if (user is null || user.EmailVerifiedAt is not null || user.NormalizedEmail != normalizedEmail) return null;
        var latest = await db.EmailVerificationTokens.Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.CreatedAt).Select(x => (DateTimeOffset?)x.CreatedAt).FirstOrDefaultAsync(ct);
        if (latest is not null && now - latest.Value < options.ResendCooldown) return null;
        await RevokePending(user.Id, now, ct);
        db.EmailVerificationTokens.Add(new()
        {
            UserId = user.Id, NormalizedEmail = user.NormalizedEmail, SecurityStamp = user.SecurityStamp,
            TokenHash = hash, CreatedAt = now, ExpiresAt = now + options.Lifetime
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(user, now + options.Lifetime);
    }

    public async Task<bool> VerifyAsync(TokenHash hash, DateTimeOffset now, CancellationToken ct)
    {
        var id = await db.EmailVerificationTokens.AsNoTracking().Where(x => x.TokenHash == hash)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (id is null) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(id.Value, ct);
        // Re-read the clock after waiting for the lock; a queued request cannot redeem an expired token.
        now = clock.GetUtcNow();
        if (user is null || user.EmailVerifiedAt is not null) return false;
        var token = await db.EmailVerificationTokens.SingleAsync(x => x.TokenHash == hash, ct);
        if (token.ConsumedAt is not null || token.RevokedAt is not null || token.CreatedAt > now || token.ExpiresAt <= now ||
            token.SecurityStamp != user.SecurityStamp || token.NormalizedEmail != user.NormalizedEmail) return false;
        token.ConsumedAt = now;
        user.EmailVerifiedAt = now;
        await db.SaveChangesAsync(ct);
        await RevokePending(user.Id, now, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private Task<User?> LockUser(Guid id, CancellationToken ct) =>
        db.Users.FromSqlInterpolated($"SELECT *, xmin FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    private Task<int> RevokePending(Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.EmailVerificationTokens.Where(x => x.UserId == userId && x.ConsumedAt == null && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.UpdatedAt, now), ct);
}
