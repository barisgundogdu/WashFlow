using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Catalog;
using WashFlow.Application.Customers;
using WashFlow.Domain.Catalog;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.Infrastructure.Catalog;

public sealed class EfCatalogStore(WashFlowDbContext db) : ICatalogStore
{
    // Preserve the authorization lock order shared with invitations, settings and customers.
    private async Task<bool> Authorized(Guid actor, Guid business, CancellationToken ct)
    {
        var b = await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\" = {business} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (b == null) return false;
        var m = await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {business} AND \"UserId\" = {actor} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (m == null || m.RevokedAt != null) return false;
        return (await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\" = {m.Id} AND \"Role\" = 1 FOR UPDATE").AsNoTracking().ToListAsync(ct)).Count == 1;
    }
    private IQueryable<BusinessService> Scoped(Guid business) => db.BusinessServices.Where(x => x.BusinessId == business && x.ArchivedAt == null);
    private ServiceView View(BusinessService s, IEnumerable<ServiceOffering> offerings) => new() {
        Id = s.Id, BusinessId = s.BusinessId, Name = s.Name, Description = s.Description, Enabled = s.Enabled, Published = s.Published,
        Version = db.Entry(s).Property<uint>("Version").CurrentValue, CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt,
        Offerings = offerings.Select(x => new OfferingInput {
            Category = x.Category, Price = CatalogValidation.Money(x.Price), Currency = x.Currency, ActiveMinutes = x.ActiveMinutes, WaitingMinutes = x.WaitingMinutes, Employees = x.Employees
        }).ToList()
    };
    private async Task<ServiceView> View(BusinessService s, CancellationToken ct) => View(s,
        await db.ServiceOfferings.AsNoTracking().Where(x => x.ServiceId == s.Id).OrderBy(x => x.Category).ToListAsync(ct));
    public async Task<ManagementResult> List(Guid? actor, Guid business, string? q, string status, int page, int size, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(actor == null ? System.Data.IsolationLevel.RepeatableRead : System.Data.IsolationLevel.ReadCommitted, ct);
        if (actor != null && !await Authorized(actor.Value, business, ct)) return new(ManagementStatus.Forbidden);
        var query = Scoped(business);
        if (actor == null) query = query.Where(x => x.Enabled && x.Published && db.ServiceOfferings.Any(o => o.ServiceId == x.Id));
        else if (status != "all") query = query.Where(x => x.Enabled == (status == "enabled"));
        if (!string.IsNullOrWhiteSpace(q)) { var key = CustomerVehicleValidation.NameSearchKey(q); query = query.Where(x => x.SearchName.Contains(key)); }
        var rows = await query.OrderBy(x => x.SearchName).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size + 1).ToListAsync(ct);
        var returned = rows.Take(size).ToList();
        var ids = returned.Select(x => x.Id).ToArray();
        var offerings = (ids.Length == 0 ? new List<ServiceOffering>() :
            await db.ServiceOfferings.AsNoTracking().Where(x => ids.Contains(x.ServiceId)).OrderBy(x => x.Category).ToListAsync(ct))
            .ToLookup(x => x.ServiceId);
        var views = new List<object>();
        foreach (var row in returned)
        {
            var view = View(row, offerings[row.Id]);
            if (actor != null) views.Add(view);
            else views.Add(new { view.Id, view.BusinessId, view.Name, view.Description, Offerings = view.Offerings!.Select(o => new { o.Category, o.Price, o.Currency, o.ActiveMinutes, o.WaitingMinutes }) });
        }
        return new(ManagementStatus.Success, new DataPage<object> { Items = views, Page = page, PageSize = size, HasMore = rows.Count > size });
    }
    public async Task<ManagementResult> Read(Guid actor, Guid business, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await Authorized(actor, business, ct)) return new(ManagementStatus.Forbidden);
        var row = await Scoped(business).SingleOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? new(ManagementStatus.NotFound) : new(ManagementStatus.Success, await View(row, ct));
    }
    public async Task<ManagementResult> Save(Guid actor, Guid business, Guid? id, ServiceInput input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await Authorized(actor, business, ct)) return new(ManagementStatus.Forbidden);
        var row = id == null ? new BusinessService { BusinessId = business, Name = "", SearchName = "" } : await Scoped(business).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return new(ManagementStatus.NotFound);
        if (id != null && db.Entry(row).Property<uint>("Version").CurrentValue != input.Version) return new(ManagementStatus.Conflict);
        row.Name = CustomerVehicleValidation.Clean(input.Name)!; row.SearchName = CustomerVehicleValidation.NameSearchKey(row.Name);
        row.Description = CustomerVehicleValidation.Clean(input.Description); row.Enabled = input.Enabled; row.Published = input.Published;
        if (id == null) db.Add(row);
        else {
            db.Entry(row).Property(x => x.UpdatedAt).IsModified = true;
            await db.ServiceOfferings.Where(x => x.ServiceId == row.Id).ExecuteDeleteAsync(ct);
        }
        db.AddRange(input.Offerings!.Select(x => new ServiceOffering { ServiceId = row.Id, Category = x.Category!, Price = CatalogValidation.Money(x.Price!), Currency = x.Currency!, ActiveMinutes = x.ActiveMinutes, WaitingMinutes = x.WaitingMinutes, Employees = x.Employees }));
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return new(ManagementStatus.Conflict); }
        var view = await View(row, ct);
        await tx.CommitAsync(ct);
        return new(ManagementStatus.Success, view);
    }
    public async Task<ManagementResult> Archive(Guid actor, Guid business, Guid id, uint version, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await Authorized(actor, business, ct)) return new(ManagementStatus.Forbidden);
        var row = await Scoped(business).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return new(ManagementStatus.NotFound);
        if (db.Entry(row).Property<uint>("Version").CurrentValue != version) return new(ManagementStatus.Conflict);
        row.ArchivedAt = DateTimeOffset.UtcNow; row.Enabled = false; row.Published = false;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return new(ManagementStatus.Conflict); }
        await tx.CommitAsync(ct);
        return new(ManagementStatus.Success);
    }
}
