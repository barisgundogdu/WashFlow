using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Customers;
using WashFlow.Domain.Customers;
using WashFlow.Infrastructure.Persistence;
using static WashFlow.Application.Customers.CustomerVehicleValidation;
namespace WashFlow.Infrastructure.Customers;

public sealed class EfCustomerVehicleStore(WashFlowDbContext db) : ICustomerVehicleStore
{
    // Business -> membership -> Owner grant; shared lock order with settings/invitations.
    private async Task<bool> Owner(Guid actor, Guid business, CancellationToken ct)
    {
        await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\"={business} FOR UPDATE").ToListAsync(ct);
        var member = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\"={business} AND \"UserId\"={actor} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (member == null || member.RevokedAt != null) return false;
        return (await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\"={member.Id} AND \"Role\"=1 FOR UPDATE").ToListAsync(ct)).Count == 1;
    }
    private async Task<Guid?> Profile(Guid actor, CancellationToken ct) => (await db.CustomerProfiles.FromSqlInterpolated($"SELECT *, xmin FROM \"CustomerProfiles\" WHERE \"UserId\"={actor} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct))?.Id;
    private IQueryable<LocalCustomer> CustomerQuery(Guid b) => db.LocalCustomers.Where(c => c.BusinessId == b);
    private IQueryable<CustomerView> CustomerViews(IQueryable<LocalCustomer> q) => q.Select(c => new CustomerView { Id = c.Id, BusinessId = c.BusinessId, FullName = c.FullName, Phone = c.Phone, Email = c.Email, Notes = c.Notes, CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt, Version = EF.Property<uint>(c, "Version") });
    private IQueryable<VehicleView> LocalViews(IQueryable<LocalVehicle> q) => q.Select(v => new VehicleView { Id = v.Id, BusinessId = v.BusinessId, LocalCustomerId = v.LocalCustomerId, CustomerName = db.LocalCustomers.Where(c => c.BusinessId == v.BusinessId && c.Id == v.LocalCustomerId).Select(c => c.FullName).Single(), Plate = v.Plate, Brand = v.Brand, Model = v.Model, Category = v.Category, ModelYear = v.ModelYear, Color = v.Color, Notes = v.Notes, CreatedAt = v.CreatedAt, UpdatedAt = v.UpdatedAt, Version = EF.Property<uint>(v, "Version") });
    private IQueryable<VehicleView> OwnedViews(IQueryable<OwnedVehicle> q) => q.Select(v => new VehicleView { Id = v.Id, Plate = v.Plate, Brand = v.Brand, Model = v.Model, Category = v.Category, ModelYear = v.ModelYear, Color = v.Color, Notes = v.Notes, CreatedAt = v.CreatedAt, UpdatedAt = v.UpdatedAt, Version = EF.Property<uint>(v, "Version") });
    private static async Task<DataPage<T>> Page<T>(IQueryable<T> q, int page, int size, CancellationToken ct)
    {
        var items = await q.Skip((page - 1) * size).Take(size + 1).ToListAsync(ct);
        return new() { Items = items.Take(size).ToArray(), Page = page, PageSize = size, HasMore = items.Count > size };
    }
    public async Task<ManagementResult> Customers(Guid actor, Guid business, string? query, int page, int size, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await Owner(actor, business, ct)) return new(ManagementStatus.Forbidden);
        var q = CustomerQuery(business);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var key = SearchKey(query); var nameKey = NameSearchKey(query); var phone = PhoneSearchKey(query);
            q = q.Where(c => c.NormalizedName.Contains(nameKey) || (phone != "" && c.NormalizedPhone != null && c.NormalizedPhone.Contains(phone)) || db.LocalVehicles.Any(v => v.BusinessId == business && v.LocalCustomerId == c.Id && v.ArchivedAt == null && v.NormalizedPlate.Contains(key)));
        }
        return new(ManagementStatus.Success, await Page(CustomerViews(q.OrderBy(c => c.NormalizedName).ThenBy(c => c.Id)), page, size, ct));
    }
    public async Task<ManagementResult> Customer(Guid actor, Guid business, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await Owner(actor, business, ct)) return new(ManagementStatus.Forbidden);
        var result = await CustomerViews(CustomerQuery(business).Where(c => c.Id == id)).SingleOrDefaultAsync(ct);
        return result == null ? new(ManagementStatus.NotFound) : new(ManagementStatus.Success, result);
    }
    public async Task<ManagementResult> SaveCustomer(Guid actor, Guid business, Guid? id, CustomerInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await Owner(actor, business, ct)) return new(ManagementStatus.Forbidden);
        var row = id == null ? new LocalCustomer { BusinessId = business, FullName = "", NormalizedName = "" } : await CustomerQuery(business).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (row == null) return new(ManagementStatus.NotFound);
        if (id != null && db.Entry(row).Property<uint>("Version").CurrentValue != input.Version) return new(ManagementStatus.Conflict);
        row.FullName = Clean(input.FullName)!; row.NormalizedName = NameSearchKey(row.FullName); row.Phone = Clean(input.Phone); row.NormalizedPhone = row.Phone == null ? null : PhoneKey(row.Phone); row.Email = Clean(input.Email); row.Notes = Clean(input.Notes);
        if (id == null) db.Add(row); else db.Entry(row).Property(c => c.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(ct); var view = await CustomerViews(CustomerQuery(business).Where(c => c.Id == row.Id)).SingleAsync(ct); await tx.CommitAsync(ct); return new(ManagementStatus.Success, view);
    }
    public async Task<ManagementResult> Vehicles(Guid actor, Guid? business, Guid? customer, string? query, int page, int size, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        IQueryable<VehicleView> view;
        var key = string.IsNullOrWhiteSpace(query) ? null : SearchKey(query);
        var nameKey = NameSearchKey(query ?? "");
        if (business is { } b)
        {
            if (!await Owner(actor, b, ct)) return new(ManagementStatus.Forbidden);
            if (customer != null && !await CustomerQuery(b).AnyAsync(c => c.Id == customer, ct)) return new(ManagementStatus.NotFound);
            var q = db.LocalVehicles.Where(v => v.BusinessId == b && v.ArchivedAt == null && (customer == null || v.LocalCustomerId == customer));
            if (key != null) { var phone = PhoneSearchKey(query!); q = q.Where(v => v.NormalizedPlate.Contains(key) || db.LocalCustomers.Any(c => c.BusinessId == b && c.Id == v.LocalCustomerId && (c.NormalizedName.Contains(nameKey) || (phone != "" && c.NormalizedPhone != null && c.NormalizedPhone.Contains(phone))))); }
            view = LocalViews(q.OrderBy(v => v.NormalizedPlate).ThenBy(v => v.Id));
        }
        else
        {
            var profile = await Profile(actor, ct); if (profile == null) return new(ManagementStatus.Forbidden);
            var q = db.OwnedVehicles.Where(v => v.CustomerProfileId == profile && v.ArchivedAt == null);
            if (key != null) q = q.Where(v => v.NormalizedPlate.Contains(key));
            view = OwnedViews(q.OrderBy(v => v.NormalizedPlate).ThenBy(v => v.Id));
        }
        return new(ManagementStatus.Success, await Page(view, page, size, ct));
    }
    private async Task<ManagementResult> SaveLocal(Guid business, Guid customer, Guid? id, VehicleInput input, CancellationToken ct)
    {
        var row = id == null ? new LocalVehicle { BusinessId = business, LocalCustomerId = customer, Plate = "", NormalizedPlate = "", Brand = "", Model = "", Category = "" } : await db.LocalVehicles.SingleOrDefaultAsync(v => v.BusinessId == business && v.LocalCustomerId == customer && v.Id == id && v.ArchivedAt == null, ct);
        if (row == null) return new(ManagementStatus.NotFound);
        if (id != null && db.Entry(row).Property<uint>("Version").CurrentValue != input.Version) return new(ManagementStatus.Conflict);
        row.Plate = PlateDisplay(input.Plate!); row.NormalizedPlate = SearchKey(row.Plate); row.Brand = Clean(input.Brand)!; row.Model = Clean(input.Model)!; row.Category = input.Category!; row.ModelYear = input.ModelYear; row.Color = Clean(input.Color); row.Notes = Clean(input.Notes);
        if (id == null) db.Add(row); else db.Entry(row).Property(v => v.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(ct);
        return new(ManagementStatus.Success, await LocalViews(db.LocalVehicles.Where(v => v.BusinessId == business && v.LocalCustomerId == customer && v.Id == row.Id)).SingleAsync(ct));
    }
    private async Task<ManagementResult> SaveOwned(Guid profile, Guid? id, VehicleInput input, CancellationToken ct)
    {
        var row = id == null ? new OwnedVehicle { CustomerProfileId = profile, Plate = "", NormalizedPlate = "", Brand = "", Model = "", Category = "" } : await db.OwnedVehicles.SingleOrDefaultAsync(v => v.CustomerProfileId == profile && v.Id == id && v.ArchivedAt == null, ct);
        if (row == null) return new(ManagementStatus.NotFound);
        if (id != null && db.Entry(row).Property<uint>("Version").CurrentValue != input.Version) return new(ManagementStatus.Conflict);
        row.Plate = PlateDisplay(input.Plate!); row.NormalizedPlate = SearchKey(row.Plate); row.Brand = Clean(input.Brand)!; row.Model = Clean(input.Model)!; row.Category = input.Category!; row.ModelYear = input.ModelYear; row.Color = Clean(input.Color); row.Notes = Clean(input.Notes);
        if (id == null) db.Add(row); else db.Entry(row).Property(v => v.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(ct);
        return new(ManagementStatus.Success, await OwnedViews(db.OwnedVehicles.Where(v => v.CustomerProfileId == profile && v.Id == row.Id)).SingleAsync(ct));
    }
    public async Task<ManagementResult> SaveVehicle(Guid actor, Guid? business, Guid? customer, Guid? id, VehicleInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        ManagementResult result;
        if (business is { } b) { if (!await Owner(actor, b, ct)) return new(ManagementStatus.Forbidden); if (customer == null || !await CustomerQuery(b).AnyAsync(c => c.Id == customer, ct)) return new(ManagementStatus.NotFound); result = await SaveLocal(b, customer.Value, id, input, ct); }
        else { var p = await Profile(actor, ct); if (p == null) return new(ManagementStatus.Forbidden); result = await SaveOwned(p.Value, id, input, ct); }
        if (result.Status == ManagementStatus.Success) await tx.CommitAsync(ct); return result;
    }
    public async Task<ManagementResult> ArchiveVehicle(Guid actor, Guid? business, Guid? customer, Guid id, uint version, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (business is { } b)
        {
            if (!await Owner(actor, b, ct)) return new(ManagementStatus.Forbidden);
            var row = await db.LocalVehicles.SingleOrDefaultAsync(v => v.BusinessId == b && v.LocalCustomerId == customer && v.Id == id && v.ArchivedAt == null, ct);
            if (row == null) return new(ManagementStatus.NotFound); if (db.Entry(row).Property<uint>("Version").CurrentValue != version) return new(ManagementStatus.Conflict); row.ArchivedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            var p = await Profile(actor, ct); if (p == null) return new(ManagementStatus.Forbidden);
            var row = await db.OwnedVehicles.SingleOrDefaultAsync(v => v.CustomerProfileId == p && v.Id == id && v.ArchivedAt == null, ct);
            if (row == null) return new(ManagementStatus.NotFound); if (db.Entry(row).Property<uint>("Version").CurrentValue != version) return new(ManagementStatus.Conflict); row.ArchivedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(ManagementStatus.Success);
    }
}
