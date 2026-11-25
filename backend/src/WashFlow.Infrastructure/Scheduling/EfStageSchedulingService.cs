using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Scheduling;
using static WashFlow.Application.Scheduling.SchedulingRules;
namespace WashFlow.Infrastructure.Scheduling;

public sealed partial class EfSchedulingService
{
    private async Task<StageTemplatesView> StageView(Guid business, Guid service, string category, CancellationToken ct)
    {
        var requirement = await db.ServiceSchedulingRequirements.SingleOrDefaultAsync(x => x.BusinessId == business && x.ServiceId == service && x.Category == category,ct);
        var rows = await db.ServiceStageTemplates.Where(x => x.BusinessId == business && x.ServiceId == service && x.Category == category && x.ArchivedAt == null).OrderBy(x => x.Sequence).ToListAsync(ct);
        return new(requirement == null ? null : View(requirement),rows.Select(x => new StageTemplateView(x.Id,business,x.Sequence,x.Name,x.Minutes,x.WorkAreaId,x.Employees)).ToArray());
    }
    public Task<StageTemplatesView> Stages(SchedulingActor actor, Guid business, Guid service, string category, CancellationToken ct = default) => Execute(actor,business,async _ =>
    {
        Found(await db.BusinessServices.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == service,ct));
        if (!WashFlow.Application.Customers.CustomerVehicleValidation.Categories.Contains(category)) Fail("validation","Araç türünü kontrol edin.",400);
        return await StageView(business,service,category,ct);
    },ct);
    public Task<StageTemplatesView> SaveStages(SchedulingActor actor, Guid business, Guid service, string category, SaveStageTemplatesInput input, CancellationToken ct = default) => Execute(actor,business,async _ =>
    {
        if (!WashFlow.Application.Customers.CustomerVehicleValidation.Categories.Contains(category) || input.Stages is not { Count: >=1 and <=20 } || input.Stages.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100 || x.Name.Any(c => char.IsControl(c) || char.IsSurrogate(c)) || x.Minutes is <1 or >10080 || x.Employees is <1 or >100 || x.WorkAreaId == Guid.Empty))
            Fail("validation","Aşama adlarını, sürelerini, alanlarını ve çalışan sayılarını kontrol edin.",400);
        var serviceRow = Found(await db.BusinessServices.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == service,ct));
        var offering = Found(await db.ServiceOfferings.SingleOrDefaultAsync(x => x.ServiceId == service && x.Category == category,ct));
        if (!serviceRow.Available) Fail("serviceUnavailable","Hizmet kullanılamıyor.");
        if (input.Stages.Sum(x => (long)x.Minutes) != offering.ActiveMinutes || input.Stages.Max(x => x.Employees) != offering.Employees)
            Fail("validation","Aşama sürelerinin toplamı katalog süresine, en yüksek çalışan sayısı katalog personel gereksinimine eşit olmalıdır.",400);
        var requirement = await db.ServiceSchedulingRequirements.SingleOrDefaultAsync(x => x.BusinessId == business && x.ServiceId == service && x.Category == category,ct);
        if (requirement == null && input.Version != null) Fail("scheduleConflict","Güncel gereksinimi yükleyin.");
        if (requirement != null) { if (input.Version == null) Fail("scheduleConflict","Güncel gereksinimi yükleyin."); Expected(requirement,input.Version.Value); }
        if (await db.Appointments.AnyAsync(x => x.BusinessId == business && x.ServiceId == service && x.Category == category && x.Status == AppointmentStatus.Confirmed && x.ActiveEndsAtUtc > clock.GetUtcNow(),ct))
            Fail("requirementInUse","Planlanmış randevular varken aşama gereksinimleri değiştirilemez.");
        foreach (var area in input.Stages.Select(x => x.WorkAreaId).Distinct()) Found(await db.WorkAreas.SingleOrDefaultAsync(x => x.BusinessId == business && x.Id == area && x.ArchivedAt == null,ct));
        var now = clock.GetUtcNow();
        if (requirement == null) { requirement = ServiceSchedulingRequirement.Create(business,service,category,input.Stages[0].WorkAreaId,false,now); db.Add(requirement); }
        requirement.EnableStagePlan(input.Stages[0].WorkAreaId,now); db.Entry(requirement).Property(x => x.UpdatedAt).IsModified = true;
        // Retain prior configuration history; free the active sequence indexes before inserting replacements.
        var old = await db.ServiceStageTemplates.Where(x => x.BusinessId == business && x.ServiceId == service && x.Category == category && x.ArchivedAt == null).ToListAsync(ct);
        foreach (var row in old) row.Archive(now); await db.SaveChangesAsync(ct);
        for (var i=0;i<input.Stages.Count;i++) { var row = input.Stages[i]; db.Add(ServiceStageTemplate.Create(business,service,category,i+1,new(row.Name,row.Minutes,row.WorkAreaId,row.Employees),now)); }
        await db.SaveChangesAsync(ct); return await StageView(business,service,category,ct);
    },ct);
}
