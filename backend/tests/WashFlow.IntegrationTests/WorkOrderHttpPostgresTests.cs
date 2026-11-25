using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WashFlow.Application.Authentication;
using WashFlow.Application.Scheduling;
using WashFlow.Application.WorkOrders;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Domain.Scheduling;
using WashFlow.Domain.WorkOrders;
using WashFlow.Infrastructure.Persistence;
using WashFlow.Infrastructure.WorkOrders;
namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class WorkOrderHttpPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Initial();
        private static DateTimeOffset Initial() { var now = DateTimeOffset.UtcNow; return new(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero); }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private WashFlowDbContext Db() => new(fixture.Options);
    private WebApplicationFactory<Program> Api(ApiFactory root, Clock clock) => root.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
        s.RemoveAll<WashFlowDbContext>(); s.AddScoped(_ => Db());
        s.RemoveAll<IStageSchedulingService>(); s.AddScoped<IStageSchedulingService>(sp => new WashFlow.Infrastructure.Scheduling.EfSchedulingService(sp.GetRequiredService<WashFlowDbContext>(), clock));
        s.RemoveAll<ISchedulingService>(); s.AddScoped<ISchedulingService>(sp => new WashFlow.Infrastructure.Scheduling.EfSchedulingService(sp.GetRequiredService<WashFlowDbContext>(), clock));
        s.RemoveAll<IWorkOrderService>(); s.AddScoped<IWorkOrderService>(sp => new EfWorkOrderService(sp.GetRequiredService<WashFlowDbContext>(), clock));
    }));
    private sealed record Data(Guid Business, Guid Appointment, uint Version, Guid Employee, DeviceSession OwnerSession, DeviceSession EmployeeSession, DeviceSession OtherSession)
    {
        public string Root => $"/api/v1/businesses/{Business}";
        public string Owner => Root + "/owner/work-orders";
        public string EmployeePath => Root + "/employee/work-orders";
        public string Create => Root + $"/appointments/{Appointment}/work-order";
        public CreateWorkOrderInput Input(Guid? request = null) => new(request ?? Guid.NewGuid(), Version, [new("Dış yıkama", 10), new("İç temizlik", 20)]);
    }
    private async Task<Data> Seed(Clock clock)
    {
        await using var db = Db(); var now = clock.Now;
        var owner = new User("Sahip", $"{Guid.NewGuid():N}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow };
        var employee = new User("Çalışan", $"{Guid.NewGuid():N}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow };
        var other = new User("Diğer", $"{Guid.NewGuid():N}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow };
        var business = new Business { Name = "İş emirleri", TimeZoneId = "Etc/UTC", HoursConfigured = true };
        var om = new BusinessMembership { BusinessId = business.Id, UserId = owner.Id };
        var em = new BusinessMembership { BusinessId = business.Id, UserId = employee.Id };
        var otherm = new BusinessMembership { BusinessId = business.Id, UserId = other.Id };
        DeviceSession Session(User user) => new() { UserId = user.Id, SecurityStamp = user.SecurityStamp, DeviceName = "Test", CreatedAt = now.AddMinutes(-1), ExpiresAt = now.AddDays(1) };
        var os = Session(owner); var es = Session(employee); var others = Session(other);
        var customer = new LocalCustomer { BusinessId = business.Id, FullName = "Müşteri", NormalizedName = "MÜŞTERİ" };
        var vehicle = new LocalVehicle { BusinessId = business.Id, LocalCustomerId = customer.Id, Plate = "34 WORK 1", NormalizedPlate = "34WORK1", Category = "Car", Brand = "Test", Model = "Test" };
        var service = new BusinessService { BusinessId = business.Id, Name = "Yıkama", SearchName = "YIKAMA", Enabled = true };
        var area = WorkArea.Create(business.Id, "Alan", 2, now.AddHours(-1));
        var appointment = Appointment.Create(business.Id, customer.Id, vehicle.Id, service.Id, area.Id, owner.Id, Guid.NewGuid(), new string('a',64), BookingSource.OwnerPhone,
            new("Yıkama", "Müşteri", vehicle.Plate, "Car", 450m, "TRY", 30, null, 1, "Etc/UTC"), now, [em.Id], now.AddMinutes(-1));
        db.AddRange(owner, employee, other, business, om, em, otherm, os, es, others, customer, vehicle, service, area, appointment,
            new BusinessMembershipRole { BusinessMembershipId = om.Id, Role = MembershipRole.Owner },
            new BusinessMembershipRole { BusinessMembershipId = em.Id, Role = MembershipRole.Employee },
            new BusinessMembershipRole { BusinessMembershipId = otherm.Id, Role = MembershipRole.Employee },
            EmployeeAvailability.Create(business.Id, em.Id, new(now.AddDays(-1), now.AddDays(1)), now.AddDays(-2)),
            EmployeeAvailability.Create(business.Id, otherm.Id, new(now.AddDays(-1), now.AddDays(1)), now.AddDays(-2)),
            new ServiceOffering { ServiceId = service.Id, Category = "Car", Price = 450m, Currency = "TRY", ActiveMinutes = 30, Employees = 1 },
            ServiceSchedulingRequirement.Create(business.Id, service.Id, "Car", area.Id, true, now.AddMinutes(-1)));
        for (var d = 1; d <= 7; d++) db.Add(new WeeklyOpeningDay { BusinessId = business.Id, Day = d, OpensAtMinute = 0, ClosesAtMinute = 1440 });
        await db.SaveChangesAsync(); return new(business.Id, appointment.Id, db.Entry(appointment).Property<uint>("Version").CurrentValue, em.Id, os, es, others);
    }
    private static HttpClient Client(WebApplicationFactory<Program> api, DeviceSession session)
    {
        var c = api.CreateClient(); c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token); return c;
    }
    private static async Task<T> Json<T>(HttpResponseMessage response)
    {
        using (response) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl?.ToString()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }
    private static async Task Fault(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode); var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Npgsql", body); Assert.DoesNotContain("Password", body);
            if (code != null) Assert.Contains($"\"code\":\"{code}\"", body);
        }
    }
    private static async Task<WorkOrderView> Act(HttpClient client, Data d, WorkOrderView order, int stage, string action, Guid? request = null) =>
        await Json<WorkOrderView>(await client.PostAsJsonAsync($"{d.EmployeePath}/{order.Id}/stages/{order.Stages[stage].Id}/{action}", new WorkActionInput(request ?? Guid.NewGuid(), order.Version)));

    [PostgresFact]
    public async Task EmployeeExecutesOrderedStagesWithPauseOverdueAuditAndSafeRetries()
    {
        var clock = new Clock(); using var root = new ApiFactory(); using var api = Api(root,clock); var d = await Seed(clock);
        using var owner = Client(api,d.OwnerSession); using var employee = Client(api,d.EmployeeSession);
        var input = d.Input(); var created = await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,input));
        Assert.Equal(created.Id,(await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,input))).Id);
        await Fault(await owner.PostAsJsonAsync(d.Create,input with { Stages = [new("Different",30)] }),HttpStatusCode.Conflict,"idempotencyConflict");
        Assert.Equal(new[] { "Ready", "Waiting" },created.Stages.Select(x=>x.Status));
        Assert.Single(await Json<WorkOrderView[]>(await employee.GetAsync(d.EmployeePath)));
        await Fault(await employee.PostAsJsonAsync($"{d.EmployeePath}/{created.Id}/stages/{created.Stages[1].Id}/start",new WorkActionInput(Guid.NewGuid(),created.Version)),HttpStatusCode.Conflict,"invalidStatus");
        var startRequest=Guid.NewGuid(); var started=await Act(employee,d,created,0,"start",startRequest);
        var retry=await Act(employee,d,created,0,"start",startRequest); Assert.Equal(started.Version,retry.Version);
        clock.Now=clock.Now.AddMinutes(3); var paused=await Act(employee,d,started,0,"pause");
        clock.Now=clock.Now.AddMinutes(5); var resumed=await Act(employee,d,paused,0,"resume");
        Assert.Equal(300000,resumed.Stages[0].PausedMilliseconds);
        clock.Now=clock.Now.AddMinutes(8); var over=await Json<WorkOrderView>(await owner.GetAsync($"{d.Owner}/{created.Id}")); Assert.True(over.PotentialConflict); Assert.Equal("InProgress",over.Status);
        var next=await Act(employee,d,resumed,0,"complete"); Assert.Equal("Ready",next.Stages[1].Status);
        var second=await Act(employee,d,next,1,"start"); clock.Now=clock.Now.AddMinutes(5);
        var completeRequest=Guid.NewGuid(); var complete=await Act(employee,d,second,1,"complete",completeRequest);
        Assert.Equal("Completed",complete.Status); Assert.NotNull(complete.CompletedAtUtc);
        await Act(employee,d,second,1,"complete",completeRequest);
        var final=await Json<WorkOrderView>(await owner.GetAsync($"{d.Owner}/{created.Id}")); Assert.Equal(7,final.Events.Count);
        Assert.All(final.Events.Where(e=>e.Action!="create"),e=>Assert.Equal(d.EmployeeSession.UserId,e.ActorUserId));
        await using var db=Db(); var appointment=await db.Appointments.SingleAsync(x=>x.Id==d.Appointment); Assert.Equal(d.Version,db.Entry(appointment).Property<uint>("Version").CurrentValue); Assert.Equal(AppointmentStatus.Confirmed,appointment.Status);
    }

    [PostgresFact]
    public async Task EmployeeOwnerAndTenantBoundariesAreEnforcedAndPricesAreNotExposed()
    {
        var clock=new Clock(); using var root=new ApiFactory(); using var api=Api(root,clock); var d=await Seed(clock); var foreign=await Seed(clock);
        using var owner=Client(api,d.OwnerSession); using var employee=Client(api,d.EmployeeSession); using var other=Client(api,d.OtherSession); using var foreignOwner=Client(api,foreign.OwnerSession); using var anonymous=api.CreateClient();
        var order=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input()));
        await Fault(await anonymous.GetAsync(d.EmployeePath),HttpStatusCode.Unauthorized);
        await Fault(await employee.GetAsync(d.Owner),HttpStatusCode.Forbidden);
        await Fault(await employee.PostAsJsonAsync(d.Create,d.Input()),HttpStatusCode.Forbidden);
        await Fault(await owner.GetAsync(d.EmployeePath),HttpStatusCode.Forbidden);
        Assert.Empty(await Json<WorkOrderView[]>(await other.GetAsync(d.EmployeePath)));
        await Fault(await other.GetAsync($"{d.EmployeePath}/{order.Id}"),HttpStatusCode.NotFound);
        await Fault(await other.PostAsJsonAsync($"{d.EmployeePath}/{order.Id}/stages/{order.Stages[0].Id}/start",new WorkActionInput(Guid.NewGuid(),order.Version)),HttpStatusCode.NotFound);
        await Fault(await foreignOwner.GetAsync($"{d.Owner}/{order.Id}"),HttpStatusCode.Forbidden);
        var text=await employee.GetStringAsync($"{d.EmployeePath}/{order.Id}"); Assert.DoesNotContain("price",text,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("450",text);
    }

    [PostgresFact]
    public async Task ConcurrentStageStartsAndCompletionsMutateOnlyOnceAndOwnerCanResolveInterruptedWork()
    {
        var clock=new Clock(); using var root=new ApiFactory(); using var api=Api(root,clock); var d=await Seed(clock);
        using var owner=Client(api,d.OwnerSession); using var employee=Client(api,d.EmployeeSession);
        var order=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input()));
        var path=$"{d.EmployeePath}/{order.Id}/stages/{order.Stages[0].Id}/start";
        var starts=await Task.WhenAll(employee.PostAsJsonAsync(path,new WorkActionInput(Guid.NewGuid(),order.Version)),employee.PostAsJsonAsync(path,new WorkActionInput(Guid.NewGuid(),order.Version)));
        Assert.Single(starts,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(starts,x=>x.StatusCode==HttpStatusCode.Conflict);
        foreach(var response in starts)response.Dispose();
        var started=await Json<WorkOrderView>(await employee.GetAsync($"{d.EmployeePath}/{order.Id}"));
        await Fault(await owner.PostAsJsonAsync($"{d.Root}/appointments/{d.Appointment}/cancel",new VersionInput(d.Version)),HttpStatusCode.Conflict,"workOrderInUse");
        var cancel=await Json<WorkOrderView>(await owner.PostAsJsonAsync($"{d.Owner}/{order.Id}/cancel",new WorkActionInput(Guid.NewGuid(),started.Version))); Assert.Equal("Cancelled",cancel.Status);
        var replacement=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input())); Assert.NotEqual(order.Id,replacement.Id);
        var first=await Act(employee,d,replacement,0,"start");clock.Now=clock.Now.AddMinutes(1);
        path=$"{d.EmployeePath}/{first.Id}/stages/{first.Stages[0].Id}/complete";
        var completes=await Task.WhenAll(employee.PostAsJsonAsync(path,new WorkActionInput(Guid.NewGuid(),first.Version)),employee.PostAsJsonAsync(path,new WorkActionInput(Guid.NewGuid(),first.Version)));
        Assert.Single(completes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(completes,x=>x.StatusCode==HttpStatusCode.Conflict);foreach(var r in completes)r.Dispose();
        await using var db=Db();Assert.Equal(1,await db.WorkOrderEvents.CountAsync(x=>x.WorkOrderId==first.Id&&x.Action=="complete"));
        var failure=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WorkOrderEvents\" SET \"Action\" = 'pause' WHERE \"WorkOrderId\" = {first.Id}")); Assert.Equal(PostgresErrorCodes.CheckViolation,failure.SqlState);
    }
    [PostgresFact]
    public async Task OverdueWorkBlocksSameEmployeeButDoesNotDoubleCountAreaAndExplicitCancelReleasesExecution()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);
        using var owner=Client(api,d.OwnerSession);using var employee=Client(api,d.EmployeeSession);
        var order=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input()));var started=await Act(employee,d,order,0,"start");
        Guid customer,vehicle,service,other;uint serviceVersion,vehicleVersion;
        await using(var db=Db())
        {
            var a=await db.Appointments.SingleAsync(x=>x.Id==d.Appointment);customer=a.LocalCustomerId;vehicle=a.LocalVehicleId;service=a.ServiceId;
            var s=await db.BusinessServices.SingleAsync(x=>x.Id==service);serviceVersion=db.Entry(s).Property<uint>("Version").CurrentValue;
            var v=await db.LocalVehicles.SingleAsync(x=>x.Id==vehicle);vehicleVersion=db.Entry(v).Property<uint>("Version").CurrentValue;
            other=await db.BusinessMemberships.Where(x=>x.BusinessId==d.Business&&x.UserId==d.OtherSession.UserId).Select(x=>x.Id).SingleAsync();
        }
        BookingInput Input(Guid who)=>new(Guid.NewGuid(),customer,vehicle,service,serviceVersion,vehicleVersion,clock.Now.ToString("yyyy-MM-dd'T'HH:mm"),null,[who]);
        // Area capacity is two: an active job plus its own planned reservation must count once.
        using(var otherBooking=await owner.PostAsJsonAsync(d.Root+"/appointments",Input(other)))Assert.Equal(HttpStatusCode.Created,otherBooking.StatusCode);
        clock.Now=clock.Now.AddMinutes(31);var candidate=Input(d.Employee);
        await Fault(await owner.PostAsJsonAsync(d.Root+"/appointments",candidate),HttpStatusCode.Conflict,"resourceConflict");
        var overdue=await Json<WorkOrderView>(await owner.GetAsync(d.Owner+"/"+order.Id));Assert.True(overdue.PotentialConflict);
        var settings=await Json<WashFlow.Application.Businesses.BusinessSettingsView>(await owner.GetAsync(d.Root+"/settings"));
        settings.WeeklyHours=settings.WeeklyHours!.Select(x=>new WashFlow.Application.Businesses.OpeningDay(x.Day,true,null,null)).ToArray();
        await Fault(await owner.PostAsJsonAsync(d.Root+"/settings",settings),HttpStatusCode.Conflict,"appointmentsOutsideHours");
        await Json<WorkOrderView>(await owner.PostAsJsonAsync($"{d.Owner}/{order.Id}/cancel",new WorkActionInput(Guid.NewGuid(),started.Version)));
        using(var retry=await owner.PostAsJsonAsync(d.Root+"/appointments",candidate))Assert.Equal(HttpStatusCode.Created,retry.StatusCode);
        await using var verify=Db();var original=await verify.Appointments.SingleAsync(x=>x.Id==d.Appointment);Assert.Equal(AppointmentStatus.Confirmed,original.Status);Assert.Equal(d.Version,verify.Entry(original).Property<uint>("Version").CurrentValue);
    }

    [PostgresFact]
    public async Task EventPersistenceFailureRollsBackStageAndRootVersion()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);
        using var owner=Client(api,d.OwnerSession);using var employee=Client(api,d.EmployeeSession);
        var order=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input()));
        await using(var db=Db())await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_work_event() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected test failure'; END $$; CREATE TRIGGER reject_work_event BEFORE INSERT ON \"WorkOrderEvents\" FOR EACH ROW EXECUTE FUNCTION reject_work_event()");
        try
        {
            await Fault(await employee.PostAsJsonAsync($"{d.EmployeePath}/{order.Id}/stages/{order.Stages[0].Id}/start",new WorkActionInput(Guid.NewGuid(),order.Version)),HttpStatusCode.InternalServerError);
        }
        finally { await using var db=Db();await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_work_event ON \"WorkOrderEvents\"; DROP FUNCTION reject_work_event()"); }
        var actual=await Json<WorkOrderView>(await owner.GetAsync(d.Owner+"/"+order.Id));Assert.Equal(order.Version,actual.Version);Assert.Equal("Ready",actual.Stages[0].Status);Assert.Single(actual.Events);
    }

    [PostgresFact]
    public async Task StagedWorkOrderKeepsDistinctAreasAndStaffAndDeniesUnassignedStageActions()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);
        using var owner=Client(api,d.OwnerSession);using var employee=Client(api,d.EmployeeSession);using var other=Client(api,d.OtherSession);
        await Json<AppointmentView>(await owner.PostAsJsonAsync($"{d.Root}/appointments/{d.Appointment}/cancel",new VersionInput(d.Version)));
        Guid service,customer,vehicle,primary,secondEmployee;uint serviceVersion,vehicleVersion;
        WorkArea secondArea;
        await using(var db=Db())
        {
            var original=await db.Appointments.SingleAsync(x=>x.Id==d.Appointment);service=original.ServiceId;customer=original.LocalCustomerId;vehicle=original.LocalVehicleId;primary=original.WorkAreaId;
            await db.ServiceOfferings.Where(x=>x.ServiceId==service).ExecuteUpdateAsync(x=>x.SetProperty(o=>o.Employees,2));
            var s=await db.BusinessServices.SingleAsync(x=>x.Id==service);serviceVersion=db.Entry(s).Property<uint>("Version").CurrentValue;
            var v=await db.LocalVehicles.SingleAsync(x=>x.Id==vehicle);vehicleVersion=db.Entry(v).Property<uint>("Version").CurrentValue;
            secondEmployee=await db.BusinessMemberships.Where(x=>x.BusinessId==d.Business&&x.UserId==d.OtherSession.UserId).Select(x=>x.Id).SingleAsync();
            secondArea=WorkArea.Create(d.Business,"İç alan",1,clock.Now);db.Add(secondArea);await db.SaveChangesAsync();
        }
        var path=$"{d.Root}/services/{service}/offerings/Car/stages";
        var current=await Json<StageTemplatesView>(await owner.GetAsync(path));
        await Fault(await employee.GetAsync(path),HttpStatusCode.Forbidden);
        await Json<StageTemplatesView>(await owner.PutAsJsonAsync(path,new SaveStageTemplatesInput(current.Requirement!.Version,[new("Dış yıkama",10,primary,1),new("İç temizlik",20,secondArea.Id,2)])));
        var staff=new[]{d.Employee,secondEmployee}.Order().ToArray();
        var appointment=await owner.PostAsJsonAsync(d.Root+"/appointments",new BookingInput(Guid.NewGuid(),customer,vehicle,service,serviceVersion,vehicleVersion,clock.Now.ToString("yyyy-MM-dd'T'HH:mm"),null,staff));
        Assert.Equal(HttpStatusCode.Created,appointment.StatusCode);var booked=(await appointment.Content.ReadFromJsonAsync<AppointmentView>())!;appointment.Dispose();
        var create=d.Root+$"/appointments/{booked.Id}/work-order";
        await Fault(await owner.PostAsJsonAsync(create,new CreateWorkOrderInput(Guid.NewGuid(),booked.Version,[new("Değişmiş",10),new("İç temizlik",20)])),HttpStatusCode.BadRequest,"validation");
        var order=await Json<WorkOrderView>(await owner.PostAsJsonAsync(create,new CreateWorkOrderInput(Guid.NewGuid(),booked.Version,[new("Dış yıkama",10),new("İç temizlik",20)])));
        Assert.Equal(primary,order.Stages[0].WorkAreaId);Assert.Equal(secondArea.Id,order.Stages[1].WorkAreaId);Assert.Equal("İç alan",order.Stages[1].WorkAreaName);
        var firstClient=staff[0]==d.Employee?employee:other;var secondClient=staff[1]==d.Employee?employee:other;
        var assigned=await Json<WorkOrderView>(await secondClient.GetAsync($"{d.EmployeePath}/{order.Id}"));Assert.Single(assigned.Stages);Assert.Equal(2,assigned.Stages[0].Sequence);
        await Fault(await secondClient.PostAsJsonAsync($"{d.EmployeePath}/{order.Id}/stages/{order.Stages[0].Id}/start",new WorkActionInput(Guid.NewGuid(),order.Version)),HttpStatusCode.Forbidden);
        var first=await Act(firstClient,d,order,0,"start");clock.Now=clock.Now.AddMinutes(10);var next=await Act(firstClient,d,first,0,"complete");
        var second=await Json<WorkOrderView>(await secondClient.PostAsJsonAsync($"{d.EmployeePath}/{order.Id}/stages/{order.Stages[1].Id}/start",new WorkActionInput(Guid.NewGuid(),next.Version)));
        Assert.Single(second.Stages);Assert.Equal("InProgress",second.Stages[0].Status);
        clock.Now=clock.Now.AddMinutes(21);
        await using(var db=Db())
        {
            var area=await db.WorkAreas.SingleAsync(x=>x.Id==secondArea.Id);var version=db.Entry(area).Property<uint>("Version").CurrentValue;db.ChangeTracker.Clear();
            var scheduling=new WashFlow.Infrastructure.Scheduling.EfSchedulingService(db,clock);
            var error=await Assert.ThrowsAsync<SchedulingFault>(()=>scheduling.ArchiveWorkArea(new(d.OwnerSession.UserId,d.OwnerSession.Id),d.Business,secondArea.Id,new(version)));
            Assert.Equal("areaInUse",error.Code);
        }
    }

    [PostgresFact]
    public async Task CancelledOrderRetainsItsPlannedTimeAfterExplicitAppointmentRescheduling()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var owner=Client(api,d.OwnerSession);
        var order=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input()));
        await Json<WorkOrderView>(await owner.PostAsJsonAsync($"{d.Owner}/{order.Id}/cancel",new WorkActionInput(Guid.NewGuid(),order.Version)));
        var moved=await Json<AppointmentView>(await owner.PostAsJsonAsync($"{d.Root}/appointments/{d.Appointment}/reschedule",new RescheduleInput(d.Version,clock.Now.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm"),null,[d.Employee])));
        Assert.NotEqual(order.PlannedStartUtc,moved.StartsAtUtc);
        var history=await Json<WorkOrderView>(await owner.GetAsync($"{d.Owner}/{order.Id}"));Assert.Equal(order.PlannedStartUtc,history.PlannedStartUtc);Assert.Equal("Cancelled",history.Status);Assert.Equal(2,history.Events.Count);
    }

}
