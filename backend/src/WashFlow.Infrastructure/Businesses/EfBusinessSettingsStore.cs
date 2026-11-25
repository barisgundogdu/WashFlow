using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Businesses;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Scheduling;
using WashFlow.Application.Scheduling;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.Infrastructure.Businesses;

// Same business -> membership -> grant lock order as employee invitations.
public sealed class EfBusinessSettingsStore(WashFlowDbContext db, TimeProvider clock) : IBusinessSettingsStore
{
    private void FreshUnitOfWork()
    {
        if (db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("İşletme ayarları bağımsız bir çalışma birimi gerektirir.");
        db.ChangeTracker.Clear();
    }
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
        FreshUnitOfWork();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var b = await Authorized(actor, business, ct);
        return b is null ? new(SettingsStatus.Forbidden) : new(SettingsStatus.Success, await View(b, ct));
    }
    public async Task<SettingsResult> UpdateAsync(Guid actor, Guid business, BusinessSettingsInput input, CancellationToken ct)
    {
        FreshUnitOfWork();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var b = await Authorized(actor, business, ct);
        if (b is null) return new(SettingsStatus.Forbidden);
        if (db.Entry(b).Property<uint>("Version").CurrentValue != input.Version) return new(SettingsStatus.Conflict);
        // The business lock serializes this check with booking/rescheduling and shift edits.
        // Validate proposed hours before changing any profile fields or replacing stored hours.
        var currentHours = await db.WeeklyOpeningDays.AsNoTracking().Where(x => x.BusinessId == business).OrderBy(x => x.Day)
            .Select(x => new OpeningDay(x.Day, x.Closed, x.OpensAtMinute, x.ClosesAtMinute)).ToListAsync(ct);
        var sameHours = b.HoursConfigured == (input.WeeklyHours != null) && b.TimeZoneId == input.TimeZoneId?.Trim() &&
            (input.WeeklyHours == null || currentHours.SequenceEqual(input.WeeklyHours.OrderBy(x => x.Day)));
        if (!sameHours && await db.WorkOrderStages.AnyAsync(x => x.BusinessId == business &&
            (x.Status == WashFlow.Domain.WorkOrders.WorkStageStatus.InProgress || x.Status == WashFlow.Domain.WorkOrders.WorkStageStatus.Paused), ct))
            return new(SettingsStatus.AppointmentsConflict);
        var now = clock.GetUtcNow();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(x => x.BusinessId == business && x.Status == AppointmentStatus.Confirmed && x.ActiveEndsAtUtc > now)
            .Select(x => new { x.StartsAtUtc, x.ActiveEndsAtUtc }).ToListAsync(ct);
        if (appointments.Count != 0)
        {
            if (input.WeeklyHours == null || string.IsNullOrWhiteSpace(input.TimeZoneId))
                return new(SettingsStatus.AppointmentsConflict);
            var zone = SchedulingRules.Zone(input.TimeZoneId.Trim());
            if (appointments.Any(x => !SchedulingRules.WithinHours(new(x.StartsAtUtc, x.ActiveEndsAtUtc), zone, input.WeeklyHours)))
                return new(SettingsStatus.AppointmentsConflict);
        }
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
