using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Businesses;
using WashFlow.Application.Catalog;
using WashFlow.Application.CustomerBooking;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Domain.Scheduling;
using WashFlow.Domain.WorkOrders;
using static WashFlow.Application.Scheduling.SchedulingRules;
namespace WashFlow.Infrastructure.Scheduling;

public sealed partial class EfSchedulingService
{
    private async Task<(User User,CustomerProfile? Profile)> CustomerIdentity(SchedulingActor actor,bool profileRequired,CancellationToken ct)
    {
        var user=await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM \"Users\" WHERE \"Id\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var session=await db.DeviceSessions.FromSqlInterpolated($"SELECT *, xmin FROM \"DeviceSessions\" WHERE \"Id\" = {actor.SessionId} AND \"UserId\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var now=clock.GetUtcNow();
        if(user==null||user.EmailVerifiedAt==null||session==null||session.RevokedAt!=null||session.CreatedAt>now||session.ExpiresAt<=now||session.SecurityStamp!=user.SecurityStamp)Fail("unauthorized","Oturum geçersiz veya süresi dolmuş.",401);
        var profile=await db.CustomerProfiles.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==actor.UserId,ct);
        if(profileRequired&&profile==null)Fail("forbidden","Müşteri hesabı gereklidir.",403);
        return(user,profile);
    }
    private async Task<T> CustomerRun<T>(SchedulingActor actor,Guid? business,bool profileRequired,Func<Business?,User,CustomerProfile?,Task<T>> operation,CancellationToken ct)
    {
        if(db.Database.CurrentTransaction!=null||db.ChangeTracker.HasChanges())throw new InvalidOperationException("Bağımsız rezervasyon işlemi gereklidir.");
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        try
        {
            Business? b=null;
            // Mutation lock order matches owner scheduling: business before identity/session/resources.
            if(business is{}id)b=Found(await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct));
            var identity=await CustomerIdentity(actor,profileRequired,ct);var result=await operation(b,identity.User,identity.Profile);await tx.CommitAsync(ct);return result;
        }
        catch(DbUpdateConcurrencyException){throw new SchedulingFault(409,"scheduleConflict","Kayıt değişti. Güncel verileri yükleyin.");}
        finally{db.ChangeTracker.Clear();}
    }
    private IQueryable<Business> Discoverable()=>db.Businesses.Where(b=>db.BusinessServices.Any(s=>s.BusinessId==b.Id&&s.Published&&s.Enabled&&s.ArchivedAt==null));
    private static DiscoveryBusiness PublicBusiness(Business b)=>new(b.Id,b.Name,b.City,b.District,b.AddressLine1,b.Phone,b.Description,b.TimeZoneId);
    public Task<IReadOnlyList<DiscoveryBusiness>> Discover(SchedulingActor actor,string? query,int page=1,CancellationToken ct=default)=>CustomerRun<IReadOnlyList<DiscoveryBusiness>>(actor,null,false,async(_,_,_)=>
    {
        if(page is<1 or>10000||query?.Length>100)Fail("validation","Arama ve sayfa bilgilerini kontrol edin.",400);
        var q=Discoverable().AsNoTracking();
        if(!string.IsNullOrWhiteSpace(query)){var pattern="%"+query.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";q=q.Where(b=>EF.Functions.ILike(b.Name,pattern,"\\")||(b.City!=null&&EF.Functions.ILike(b.City,pattern,"\\"))||(b.District!=null&&EF.Functions.ILike(b.District,pattern,"\\")));}
        return(await q.OrderBy(b=>b.Name).ThenBy(b=>b.Id).Skip((page-1)*20).Take(20).ToListAsync(ct)).Select(PublicBusiness).ToArray();
    },ct);
    public Task<CustomerBusinessDetail> Business(SchedulingActor actor,Guid business,int page=1,CancellationToken ct=default)=>CustomerRun(actor,null,false,async(_,_,_)=>
    {
        if(page is<1 or>10000)Fail("validation","Geçerli sayfa seçin.",400);
        var b=Found(await Discoverable().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==business,ct));
        var services=await db.BusinessServices.Where(x=>x.BusinessId==business&&x.Enabled&&x.Published&&x.ArchivedAt==null).OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*20).Take(21).ToListAsync(ct);
        var ids=services.Take(20).Select(x=>x.Id).ToArray();var offers=await db.ServiceOfferings.AsNoTracking().Where(x=>ids.Contains(x.ServiceId)).ToListAsync(ct);
        var requirements=await db.ServiceSchedulingRequirements.AsNoTracking().Where(x=>x.BusinessId==business&&ids.Contains(x.ServiceId)).ToListAsync(ct);
        var areas=await db.WorkAreas.AsNoTracking().Where(x=>x.BusinessId==business&&x.ArchivedAt==null).Select(x=>x.Id).ToArrayAsync(ct);
        var views=services.Take(20).Select(s=>new PublishedService(s.Id,Version(s),s.Name,s.Description,offers.Where(o=>o.ServiceId==s.Id).OrderBy(o=>o.Category).Select(o=>new PublishedOffering(o.Category,CatalogValidation.Money(o.Price),o.Currency,o.ActiveMinutes,o.WaitingMinutes,o.Employees,
            b.HoursConfigured&&b.TimeZoneId!=null&&requirements.Any(r=>r.ServiceId==s.Id&&r.Category==o.Category&&(r.SingleStageConfirmed||r.StagePlanEnabled)&&areas.Contains(r.WorkAreaId)))).ToArray())).ToArray();
        var hours=b.HoursConfigured?await db.WeeklyOpeningDays.AsNoTracking().Where(x=>x.BusinessId==business).OrderBy(x=>x.Day).Select(x=>new OpeningDay(x.Day,x.Closed,x.OpensAtMinute,x.ClosesAtMinute)).ToListAsync(ct):null;
        return new CustomerBusinessDetail(PublicBusiness(b),hours,views,b.CancellationRescheduleCutoffMinutes,page,services.Count>20);
    },ct);
    private async Task RequirePublished(Guid business,Guid service,CancellationToken ct)
    {if(!await db.BusinessServices.AnyAsync(x=>x.BusinessId==business&&x.Id==service&&x.Published&&x.Enabled&&x.ArchivedAt==null,ct))Fail("resourceNotFound","Hizmet bulunamadı.",404);}
    private async Task<OwnedVehicle> Owned(Guid profile,Guid id,CancellationToken ct)=>Found(await db.OwnedVehicles.FromSqlInterpolated($"SELECT *, xmin FROM \"OwnedVehicles\" WHERE \"Id\" = {id} AND \"CustomerProfileId\" = {profile} FOR SHARE").SingleOrDefaultAsync(ct));
    private async Task<Guid[]> Roster(Guid business,CancellationToken ct)=>await(from m in db.BusinessMemberships.AsNoTracking() join role in db.BusinessMembershipRoles.AsNoTracking() on m.Id equals role.BusinessMembershipId
        where m.BusinessId==business&&m.RevokedAt==null&&role.Role==MembershipRole.Employee orderby m.Id select m.Id).Distinct().ToArrayAsync(ct);
    private async Task<(Guid[] Staff,Guid[][]? Assignments)?> AutoAllocate(Business b,WorkArea primary,Occupancy interval,int employees,IReadOnlyList<StageResourceSnapshot>? plans,Guid[] candidates,Guid? exclude,ResourceCapacity.Snapshot snapshot,IReadOnlyList<OpeningDay> hours,IReadOnlyDictionary<Guid,WorkArea> areas,CancellationToken ct)
    {
        if(!b.HoursConfigured)Fail("hoursUnconfigured","İşletmenin çalışma saatleri henüz yapılandırılmamış.");
        if(candidates.Length<employees)return null;
        if(plans==null)
        {
            if(!WithinHours(interval,Zone(b.TimeZoneId),hours))return null;
            var crew=snapshot.Choose(primary,interval,candidates,employees,exclude);return crew==null?null:(crew,null);
        }
        Guid[][]? assigned;
        try{assigned=await PlanResources(b,primary,interval,candidates,plans,exclude,ct,snapshot,hours,areas);}
        catch(SchedulingFault e)when(e.Code=="outsideHours"){return null;}
        if(assigned==null)return null;var pool=assigned.SelectMany(x=>x).Distinct().Order().ToArray();
        return pool.Length>100?null:(pool,assigned);
    }
    private async Task<IReadOnlyList<OpeningDay>> Hours(Guid business,CancellationToken ct)=>await db.WeeklyOpeningDays.AsNoTracking().Where(x=>x.BusinessId==business).OrderBy(x=>x.Day).Select(x=>new OpeningDay(x.Day,x.Closed,x.OpensAtMinute,x.ClosesAtMinute)).ToListAsync(ct);
    private async Task<IReadOnlyDictionary<Guid,WorkArea>> Areas(Guid business,CancellationToken ct)=>await db.WorkAreas.AsNoTracking().Where(x=>x.BusinessId==business&&x.ArchivedAt==null).ToDictionaryAsync(x=>x.Id,ct);
    private static void ValidInstant(DateTimeOffset start)
    {if(start.UtcTicks%TimeSpan.TicksPerMinute!=0)Fail("validation","Randevu saati dakika hassasiyetinde olmalıdır.",400);}
    private static string CustomerHash(CustomerBookingInput input)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Source="Customer",input.OwnedVehicleId,input.ServiceId,input.ExpectedServiceVersion,input.ExpectedVehicleVersion,Start=input.StartsAtUtc.ToUniversalTime()}))));
    public Task<CustomerSlotsView> Slots(SchedulingActor actor,Guid business,Guid service,Guid vehicle,DateOnly date,int page=1,int stepMinutes=15,Guid? appointmentId=null,CancellationToken ct=default)=>CustomerRun(actor,null,true,async(_,_,profile)=>
    {
        if(page is<1 or>10000||stepMinutes is<1 or>60)Fail("validation","Saat aralığını ve sayfayı kontrol edin.",400);
        var b=Found(await db.Businesses.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==business,ct));var owned=await Owned(profile!.Id,vehicle,ct);
        if(owned.ArchivedAt!=null)Fail("resourceNotFound","Araç bulunamadı.",404);
        Appointment? original=null;
        if(appointmentId is{}id){original=Found(await Aggregates(business).SingleOrDefaultAsync(x=>x.Id==id&&x.CustomerProfileId==profile.Id&&x.OwnedVehicleId==vehicle&&x.ServiceId==service,ct));RequireCustomerChange(b,original);}
        if(original==null)await RequirePublished(business,service,ct);
        var config=await Configuration(business,service,owned.Category,ct);
        if(original==null&&!config.Service.Published)Fail("resourceNotFound","Hizmet bulunamadı.",404);
        if(!b.HoursConfigured)Fail("hoursUnconfigured","İşletmenin çalışma saatleri henüz yapılandırılmamış.");
        var plans=original==null?config.Plans:original.Stages.Count==0?null:original.Stages.Select(x=>new StageResourceSnapshot(x.Name,x.Minutes,x.WorkAreaId,x.Employees)).ToArray();
        var duration=original?.ActiveMinutes??config.Offering.ActiveMinutes;var staffCount=original?.Employees??config.Offering.Employees;
        var primary=original==null?config.Area:Found(await db.WorkAreas.AsNoTracking().SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==original.WorkAreaId&&x.ArchivedAt==null,ct));
        var hours=await Hours(business,ct);var zone=Zone(b.TimeZoneId);var day=hours.SingleOrDefault(x=>x.Day==((int)date.DayOfWeek+6)%7+1);
        CustomerSlotsView Result(IReadOnlyList<CustomerSlot> items,bool more)=>new(business,service,vehicle,owned.Plate,original?.ServiceName??config.Service.Name,owned.Category,
            CatalogValidation.Money(original?.Price??config.Offering.Price),original?.Currency??config.Offering.Currency,duration,Version(config.Service),Version(owned),items,page,more,stepMinutes);
        if(day==null||day.Closed)return Result([],false);
        var candidates=new List<DateTimeOffset>();var now=clock.GetUtcNow();
        for(var minute=day.OpensAtMinute!.Value;minute<day.ClosesAtMinute!.Value;minute+=stepMinutes)
        {
            var wall=DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute),DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(wall))continue;
            foreach(var offset in zone.IsAmbiguousTime(wall)?zone.GetAmbiguousTimeOffsets(wall):[zone.GetUtcOffset(wall)])
            {try{var start=new DateTimeOffset(wall,offset).ToUniversalTime();if(start>=now)candidates.Add(start);}catch(ArgumentException){}}
        }
        candidates=candidates.Distinct().Order().ToList();if(candidates.Count==0||(page-1)*12>=candidates.Count)return Result([],false);
        var roster=await Roster(business,ct);if(roster.Length<staffCount)return Result([],false);
        var areas=await Areas(business,ct);var latest=Interval(candidates[^1],duration).End;
        var snapshot=await ResourceCapacity.Load(db,business,candidates[0],latest,roster,now,ct);var found=new List<CustomerSlot>();var skip=(page-1)*12;var seen=0;
        foreach(var start in candidates)
        {
            var interval=Interval(start,duration);if(await AutoAllocate(b,primary,interval,staffCount,plans,roster,appointmentId,snapshot,hours,areas,ct)==null)continue;
            if(seen++<skip)continue;var local=TimeZoneInfo.ConvertTime(start,zone);found.Add(new(start,interval.End,local.ToString("HH:mm zzz",CultureInfo.InvariantCulture)));
            if(found.Count==13)break;
        }
        return Result(found.Take(12).ToArray(),found.Count>12);
    },ct);
    public Task<SchedulingResult<CustomerBookingView>> Book(SchedulingActor actor,Guid business,CustomerBookingInput input,CancellationToken ct=default)=>CustomerRun(actor,business,true,async(b,user,profile)=>
    {
        if(input.ClientRequestId==Guid.Empty||input.OwnedVehicleId==Guid.Empty||input.ServiceId==Guid.Empty||input.ExpectedServiceVersion==0||input.ExpectedVehicleVersion==0)Fail("validation","Araç, hizmet ve güncel kayıt sürümü gereklidir.",400);
        ValidInstant(input.StartsAtUtc);var hash=CustomerHash(input);
        var prior=await Aggregates(business).SingleOrDefaultAsync(x=>x.ClientRequestId==input.ClientRequestId,ct);
        if(prior!=null)
        {if(prior.Source!=BookingSource.Customer||prior.CustomerProfileId!=profile!.Id||prior.CreatedByUserId!=actor.UserId||prior.RequestHash!=hash)Fail("idempotencyConflict","İstek anahtarı farklı rezervasyon için kullanılmış.");return new SchedulingResult<CustomerBookingView>(await CustomerView(prior,b!,ct),false);}
        var owned=await Owned(profile!.Id,input.OwnedVehicleId,ct);if(owned.ArchivedAt!=null)Fail("resourceNotFound","Araç bulunamadı.",404);
        await RequirePublished(business,input.ServiceId,ct);
        var config=await Configuration(business,input.ServiceId,owned.Category,ct);if(!config.Service.Published)Fail("resourceNotFound","Hizmet bulunamadı.",404);
        Expected(config.Service,input.ExpectedServiceVersion);Expected(owned,input.ExpectedVehicleVersion);
        var start=input.StartsAtUtc.ToUniversalTime();if(start<clock.GetUtcNow())Fail("slotUnavailable","Seçilen saat geçmiş. Uygun saatleri yenileyin.");
        if(!b!.HoursConfigured)Fail("hoursUnconfigured","İşletmenin çalışma saatleri henüz yapılandırılmamış.");
        var interval=Interval(start,config.Offering.ActiveMinutes);var candidates=await Roster(business,ct);var hours=await Hours(business,ct);var areas=await Areas(business,ct);
        var resources=await ResourceCapacity.Load(db,business,interval.Start,interval.End,candidates,clock.GetUtcNow(),ct);
        var allocation=await AutoAllocate(b!,config.Area,interval,config.Offering.Employees,config.Plans,candidates,null,resources,hours,areas,ct);
        if(allocation==null)Fail("resourceConflict","Seçilen saat artık uygun değil. Saatleri yenileyin.");
        foreach(var employee in allocation.Value.Staff)await Employee(business,employee,ct);
        var snapshot=new AppointmentSnapshot(config.Service.Name,user.FullName,owned.Plate,owned.Category,config.Offering.Price,config.Offering.Currency,config.Offering.ActiveMinutes,config.Offering.WaitingMinutes,config.Offering.Employees,Zone(b!.TimeZoneId).Id);
        var row=Appointment.Create(business,Guid.Empty,Guid.Empty,input.ServiceId,config.Area.Id,actor.UserId,input.ClientRequestId,hash,BookingSource.Customer,snapshot,start,allocation.Value.Staff,clock.GetUtcNow(),config.Plans,allocation.Value.Assignments,profile.Id,owned.Id);
        db.Add(row);await db.SaveChangesAsync(ct);db.Add(new SchedulingCreateReceipt{BusinessId=business,ClientRequestId=input.ClientRequestId,AppointmentId=row.Id,ResultJson=JsonSerializer.Serialize(View(row))});await db.SaveChangesAsync(ct);
        return new SchedulingResult<CustomerBookingView>(await CustomerView(row,b,ct),true);
    },ct);
    private bool WindowOpen(Business business,Appointment a)=>a.Status==AppointmentStatus.Confirmed&&a.StartsAtUtc-clock.GetUtcNow()>=TimeSpan.FromMinutes(business.CancellationRescheduleCutoffMinutes);
    private void RequireCustomerChange(Business b,Appointment a){if(!WindowOpen(b,a))Fail("bookingChangeCutoff","İptal ve erteleme süresi dolmuş. İşletmeyle görüşün.");}
    private async Task<CustomerBookingView> CustomerView(Appointment a,Business b,CancellationToken ct)
    {
        var work=await db.WorkOrders.AsNoTracking().Where(x=>x.BusinessId==b.Id&&x.AppointmentId==a.Id&&x.CancelledAtUtc==null).Select(x=>new{x.CompletedAtUtc,Started=x.Stages.Any(s=>s.StartedAtUtc!=null)}).SingleOrDefaultAsync(ct);
        var workStatus=work==null?"NotStarted":work.CompletedAtUtc!=null?"Completed":work.Started?"InProgress":"Scheduled";
        var zone=Zone(string.IsNullOrWhiteSpace(b.TimeZoneId)?a.TimeZoneIdSnapshot:b.TimeZoneId);
        return new(a.Id,b.Id,b.Name,a.OwnedVehicleId!.Value,a.ServiceId,Version(a),a.VehiclePlate,a.ServiceName,CatalogValidation.Money(a.Price),a.Currency,a.ActiveMinutes,a.Status.ToString(),workStatus,a.StartsAtUtc,a.ActiveEndsAtUtc,zone.Id,
            TimeZoneInfo.ConvertTime(a.StartsAtUtc,zone).ToString("dd.MM.yyyy HH:mm zzz",CultureInfo.InvariantCulture),b.CancellationRescheduleCutoffMinutes,WindowOpen(b,a)&&work?.Started!=true&&work?.CompletedAtUtc==null,clock.GetUtcNow());
    }
    public Task<IReadOnlyList<CustomerBookingView>> Mine(SchedulingActor actor,string status="all",int page=1,CancellationToken ct=default)=>CustomerRun<IReadOnlyList<CustomerBookingView>>(actor,null,true,async(_,_,profile)=>
    {
        if(page is<1 or>10000||status is not("all" or"upcoming" or"history"))Fail("validation","Randevu filtresini kontrol edin.",400);
        var now=clock.GetUtcNow();var q=db.Appointments.Where(x=>x.CustomerProfileId==profile!.Id&&x.Source==BookingSource.Customer);
        if(status=="upcoming")q=q.Where(a=>a.Status==AppointmentStatus.Confirmed&&(a.ActiveEndsAtUtc>now||db.WorkOrders.Any(w=>w.BusinessId==a.BusinessId&&w.AppointmentId==a.Id&&w.CancelledAtUtc==null&&w.CompletedAtUtc==null))&&!db.WorkOrders.Any(w=>w.BusinessId==a.BusinessId&&w.AppointmentId==a.Id&&w.CompletedAtUtc!=null));
        if(status=="history")q=q.Where(a=>a.Status==AppointmentStatus.Cancelled||db.WorkOrders.Any(w=>w.BusinessId==a.BusinessId&&w.AppointmentId==a.Id&&w.CompletedAtUtc!=null)||(a.ActiveEndsAtUtc<=now&&!db.WorkOrders.Any(w=>w.BusinessId==a.BusinessId&&w.AppointmentId==a.Id&&w.CancelledAtUtc==null&&w.CompletedAtUtc==null)));
        var rows=await q.OrderByDescending(a=>a.StartsAtUtc).ThenBy(a=>a.Id).Skip((page-1)*20).Take(20).ToListAsync(ct);var result=new List<CustomerBookingView>();
        foreach(var row in rows)result.Add(await CustomerView(row,Found(await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b=>b.Id==row.BusinessId,ct)),ct));return result;
    },ct);
    public Task<CustomerBookingView> OwnDetail(SchedulingActor actor,Guid id,CancellationToken ct=default)=>CustomerRun(actor,null,true,async(_,_,profile)=>
    {var row=Found(await db.Appointments.SingleOrDefaultAsync(x=>x.Id==id&&x.CustomerProfileId==profile!.Id&&x.Source==BookingSource.Customer,ct));return await CustomerView(row,Found(await db.Businesses.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.BusinessId,ct)),ct);},ct);
    private async Task<Guid> OwnBusiness(SchedulingActor actor,Guid id,CancellationToken ct)=>await(from a in db.Appointments.AsNoTracking() join p in db.CustomerProfiles.AsNoTracking() on a.CustomerProfileId equals p.Id
        where a.Id==id&&a.Source==BookingSource.Customer&&p.UserId==actor.UserId select(Guid?)a.BusinessId).SingleOrDefaultAsync(ct)??throw new SchedulingFault(404,"resourceNotFound","Randevu bulunamadı.");
    private async Task RetirePendingWork(Appointment a,SchedulingActor actor,Guid request,CancellationToken ct)
    {
        var work=await db.WorkOrders.Where(x=>x.BusinessId==a.BusinessId&&x.AppointmentId==a.Id&&x.CancelledAtUtc==null).Include(x=>x.Stages).ThenInclude(x=>x.Assignments).Include(x=>x.Events).SingleOrDefaultAsync(ct);
        if(work==null)return;if(work.CompletedAtUtc!=null||work.Stages.Any(x=>x.StartedAtUtc!=null))Fail("workOrderInUse","İşlem başlamış. Değişiklik için işletmeyle görüşün.");
        if(await db.WorkOrderEvents.AnyAsync(x=>x.BusinessId==a.BusinessId&&x.RequestId==request,ct))Fail("idempotencyConflict","İstek anahtarı farklı işlem için kullanılmış.");
        work.Cancel(actor.UserId,request,clock.GetUtcNow());db.Entry(work).Property(x=>x.UpdatedAt).IsModified=true;db.Entry(work.Events[^1]).State=EntityState.Added;
    }
    public async Task<CustomerBookingView> CancelOwn(SchedulingActor actor,Guid id,CustomerChangeInput input,CancellationToken ct=default)
    {
        var business=await OwnBusiness(actor,id,ct);return await CustomerRun(actor,business,true,async(b,_,profile)=>
        {
            if(input.RequestId==Guid.Empty||input.Version==0)Fail("validation","Geçerli işlem ve kayıt sürümü gereklidir.",400);
            var row=Found(await Aggregates(business).SingleOrDefaultAsync(x=>x.Id==id&&x.CustomerProfileId==profile!.Id&&x.Source==BookingSource.Customer,ct));
            if(row.Status==AppointmentStatus.Cancelled)return await CustomerView(row,b!,ct);
            Expected(row,input.Version);RequireCustomerChange(b!,row);await RetirePendingWork(row,actor,input.RequestId,ct);
            row.Cancel(actor.UserId,clock.GetUtcNow());db.Entry(row).Property(x=>x.UpdatedAt).IsModified=true;db.Entry(row.Events[^1]).State=EntityState.Added;
            await db.SaveChangesAsync(ct);return await CustomerView(row,b!,ct);
        },ct);
    }
    public async Task<CustomerBookingView> RescheduleOwn(SchedulingActor actor,Guid id,CustomerRescheduleInput input,CancellationToken ct=default)
    {
        var business=await OwnBusiness(actor,id,ct);return await CustomerRun(actor,business,true,async(b,_,profile)=>
        {
            if(input.RequestId==Guid.Empty||input.Version==0)Fail("validation","Geçerli işlem ve kayıt sürümü gereklidir.",400);ValidInstant(input.StartsAtUtc);
            var row=Found(await Aggregates(business).SingleOrDefaultAsync(x=>x.Id==id&&x.CustomerProfileId==profile!.Id&&x.Source==BookingSource.Customer,ct));Expected(row,input.Version);RequireCustomerChange(b!,row);
            var owned=await Owned(profile!.Id,row.OwnedVehicleId!.Value,ct);if(owned.ArchivedAt!=null||owned.Category!=row.Category)Fail("vehicleChanged","Araç kaydı değişmiş. İşletmeyle görüşün.");
            await Configuration(business,row.ServiceId,row.Category,ct);var start=input.StartsAtUtc.ToUniversalTime();if(start<clock.GetUtcNow())Fail("slotUnavailable","Geçmiş bir saate randevu taşınamaz.");
            var interval=Interval(start,row.ActiveMinutes);var primary=Found(await db.WorkAreas.AsNoTracking().SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==row.WorkAreaId&&x.ArchivedAt==null,ct));
            var plans=row.Stages.Count==0?null:row.Stages.Select(x=>new StageResourceSnapshot(x.Name,x.Minutes,x.WorkAreaId,x.Employees)).ToArray();
            var candidates=await Roster(business,ct);var hours=await Hours(business,ct);var areas=await Areas(business,ct);var resources=await ResourceCapacity.Load(db,business,interval.Start,interval.End,candidates,clock.GetUtcNow(),ct);
            var allocation=await AutoAllocate(b!,primary,interval,row.Employees,plans,candidates,row.Id,resources,hours,areas,ct);if(allocation==null)Fail("resourceConflict","Seçilen saat artık uygun değil.");
            foreach(var employee in allocation.Value.Staff)await Employee(business,employee,ct);
            await RetirePendingWork(row,actor,input.RequestId,ct);var now=clock.GetUtcNow();foreach(var reservation in row.Reservations.Where(x=>x.ReleasedAt==null))reservation.Release(now);await db.SaveChangesAsync(ct);
            row.Reschedule(start,allocation.Value.Staff,actor.UserId,now,allocation.Value.Assignments);db.Entry(row).Property(x=>x.UpdatedAt).IsModified=true;
            foreach(var reservation in row.Reservations.Where(x=>x.ReleasedAt==null))db.Entry(reservation).State=EntityState.Added;db.Entry(row.Events[^1]).State=EntityState.Added;
            await db.SaveChangesAsync(ct);return await CustomerView(row,b!,ct);
        },ct);
    }
}
