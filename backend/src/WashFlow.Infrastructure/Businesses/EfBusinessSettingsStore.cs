using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Businesses;
using WashFlow.Domain.Businesses;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Businesses;

// Same business -> membership -> grant lock order as employee invitations.
public sealed class EfBusinessSettingsStore(WashFlowDbContext db) : IBusinessSettingsStore
{
    private async Task<Business?> Authorized(Guid actor, Guid id, CancellationToken ct)
    {
        var business = await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (business is null) return null;
        var membership = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {id} AND \"UserId\" = {actor} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (membership is null || membership.RevokedAt != null) return null;
        var grants = await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\" = {membership.Id} AND \"Role\" = 1 FOR UPDATE").AsNoTracking().ToListAsync(ct);
        return grants.Count == 1 ? business : null;
    }
    private async Task<BusinessSettingsView> View(Business b, CancellationToken ct) => new()
    {
        BusinessId = b.Id,
        Name = b.Name,
        Version = db.Entry(b).Property<uint>("Version").CurrentValue,
        Phone = b.Phone,
        ContactEmail = b.ContactEmail,
        AddressLine1 = b.AddressLine1,
        AddressLine2 = b.AddressLine2,
        District = b.District,
        City = b.City,
        Region = b.Region,
        PostalCode = b.PostalCode,
        CountryCode = b.CountryCode,
        Description = b.Description,
        TimeZoneId = b.TimeZoneId,
        WeeklyHours = !b.HoursConfigured ? null : await db.WeeklyOpeningDays.Where(x => x.BusinessId == b.Id).OrderBy(x => x.Day).Select(x => new OpeningDay(x.Day, x.Closed, x.OpensAtMinute, x.ClosesAtMinute)).ToListAsync(ct)
    };
    public async Task<SettingsResult> ReadAsync(Guid actor, Guid business, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var b = await Authorized(actor, business, ct);
        return b is null ? new(SettingsStatus.Forbidden) : new(SettingsStatus.Success, await View(b, ct));
    }
    public async Task<SettingsResult> UpdateAsync(Guid actor, Guid business, BusinessSettingsInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var b = await Authorized(actor, business, ct);
        if (b is null) return new(SettingsStatus.Forbidden);
        if (db.Entry(b).Property<uint>("Version").CurrentValue != input.Version) return new(SettingsStatus.Conflict);
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        b.Name = input.Name!.Trim();
        b.Phone = Clean(input.Phone);
        b.ContactEmail = Clean(input.ContactEmail);
        b.AddressLine1 = Clean(input.AddressLine1);
        b.AddressLine2 = Clean(input.AddressLine2);
        b.District = Clean(input.District);
        b.City = Clean(input.City);
        b.Region = Clean(input.Region);
        b.PostalCode = Clean(input.PostalCode);
        b.CountryCode = Clean(input.CountryCode);
        b.Description = Clean(input.Description);
        b.TimeZoneId = Clean(input.TimeZoneId);
        b.CountryCode = b.CountryCode?.ToUpperInvariant();
        b.HoursConfigured = input.WeeklyHours != null;
        // Always advance the parent xmin, even for hours-only or identical saves.
        db.Entry(b).Property(x => x.UpdatedAt).IsModified = true;
        await db.WeeklyOpeningDays.Where(x => x.BusinessId == business).ExecuteDeleteAsync(ct);
        if (input.WeeklyHours != null) db.AddRange(input.WeeklyHours.Select(x => new WeeklyOpeningDay { BusinessId = business, Day = x.Day, Closed = x.Closed, OpensAtMinute = x.OpensAtMinute, ClosesAtMinute = x.ClosesAtMinute }));
        await db.SaveChangesAsync(ct);
        var view = await View(b, ct);
        await tx.CommitAsync(ct);
        return new(SettingsStatus.Success, view);
    }
}
