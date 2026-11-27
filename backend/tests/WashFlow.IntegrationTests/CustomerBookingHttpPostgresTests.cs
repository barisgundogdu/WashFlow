using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WashFlow.Application.Authentication;
using WashFlow.Application.CustomerBooking;
using WashFlow.Infrastructure.Scheduling;
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
public sealed class CustomerBookingHttpPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Initial();
        private static DateTimeOffset Initial(){var n=DateTimeOffset.UtcNow;return new(n.UtcTicks-n.UtcTicks%TimeSpan.TicksPerMinute,TimeSpan.Zero);}
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private WashFlowDbContext Db()=>new(fixture.Options);
    private WebApplicationFactory<Program> Api(ApiFactory root,Clock clock)=>root.WithWebHostBuilder(b=>b.ConfigureServices(s=>
    {
        s.RemoveAll<WashFlowDbContext>();s.AddScoped(_=>Db());
        s.RemoveAll<ICustomerBookingService>();s.AddScoped<ICustomerBookingService>(sp=>new EfSchedulingService(sp.GetRequiredService<WashFlowDbContext>(),clock));
        s.RemoveAll<ISchedulingService>();s.AddScoped<ISchedulingService>(sp=>new EfSchedulingService(sp.GetRequiredService<WashFlowDbContext>(),clock));
        s.RemoveAll<IWorkOrderService>();s.AddScoped<IWorkOrderService>(sp=>new EfWorkOrderService(sp.GetRequiredService<WashFlowDbContext>(),clock));
    }));
    private sealed record Data(Guid Business,Guid Service,Guid Hidden,Guid Vehicle,Guid Profile,Guid Employee,Guid LocalCustomer,Guid LocalVehicle,uint ServiceVersion,uint VehicleVersion,DeviceSession Customer,DeviceSession Stranger,DeviceSession Owner)
    {
        public string Root=>$"/api/v1/customer/businesses/{Business}";
        public CustomerBookingInput Input(DateTimeOffset start)=>new(Guid.NewGuid(),Vehicle,Service,ServiceVersion,VehicleVersion,start);
    }
    private async Task<Data> Seed(Clock clock)
    {
        await using var db=Db();var n=clock.Now;
        User User(string name)=>new(name,$"{Guid.NewGuid():N}@example.test","hash"){EmailVerifiedAt=DateTimeOffset.UtcNow};
        var owner=User("Sahip");var customer=User("Hesap sahibi");var stranger=User("Başka müşteri");var worker=User("Çalışan");
        DeviceSession Session(User u)=>new(){UserId=u.Id,SecurityStamp=u.SecurityStamp,DeviceName="Test",CreatedAt=n.AddMinutes(-1),ExpiresAt=n.AddDays(2)};
        var os=Session(owner);var cs=Session(customer);var ss=Session(stranger);
        var profile=new CustomerProfile{UserId=customer.Id};var other=new CustomerProfile{UserId=stranger.Id};
        var b=new Business{Name="Müşteri randevusu "+Guid.NewGuid().ToString("N"),TimeZoneId="Etc/UTC",HoursConfigured=true,City="İstanbul"};
        var om=new BusinessMembership{BusinessId=b.Id,UserId=owner.Id};var em=new BusinessMembership{BusinessId=b.Id,UserId=worker.Id};
        var vehicle=new OwnedVehicle{CustomerProfileId=profile.Id,Plate="34 OWN 1",NormalizedPlate="34OWN1",Brand="Test",Model="Test",Category="Car"};
        // Matching plates/names are intentionally insufficient to link platform identity to tenant records.
        var local=new LocalCustomer{BusinessId=b.Id,FullName=customer.FullName,NormalizedName="HESAP SAHİBİ"};
        var lv=new LocalVehicle{BusinessId=b.Id,LocalCustomerId=local.Id,Plate=vehicle.Plate,NormalizedPlate=vehicle.NormalizedPlate,Brand="Test",Model="Test",Category="Car"};
        var service=new BusinessService{BusinessId=b.Id,Name="Yıkama",SearchName="YIKAMA",Enabled=true,Published=true};
        var hidden=new BusinessService{BusinessId=b.Id,Name="Gizli",SearchName="GİZLİ",Enabled=true,Published=false};
        var area=WorkArea.Create(b.Id,"Yıkama alanı",2,n);
        db.AddRange(owner,customer,stranger,worker,os,cs,ss,profile,other,b,om,em,vehicle,local,lv,service,hidden,area,
            new BusinessMembershipRole{BusinessMembershipId=om.Id,Role=MembershipRole.Owner},new BusinessMembershipRole{BusinessMembershipId=em.Id,Role=MembershipRole.Employee},
            EmployeeAvailability.Create(b.Id,em.Id,new(n.AddDays(-1),n.AddDays(3)),n),
            new ServiceOffering{ServiceId=service.Id,Category="Car",Price=450.25m,Currency="TRY",ActiveMinutes=30,Employees=1},
            ServiceSchedulingRequirement.Create(b.Id,service.Id,"Car",area.Id,true,n));
        for(var d=1;d<=7;d++)db.Add(new WeeklyOpeningDay{BusinessId=b.Id,Day=d,OpensAtMinute=0,ClosesAtMinute=1440});
        await db.SaveChangesAsync();return new(b.Id,service.Id,hidden.Id,vehicle.Id,profile.Id,em.Id,local.Id,lv.Id,db.Entry(service).Property<uint>("Version").CurrentValue,db.Entry(vehicle).Property<uint>("Version").CurrentValue,cs,ss,os);
    }
    private static HttpClient Client(WebApplicationFactory<Program> api,DeviceSession session)
    {var c=api.CreateClient();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token);return c;}
    private static async Task<T> Json<T>(HttpResponseMessage r,HttpStatusCode expected=HttpStatusCode.OK)
    {using(r){Assert.Equal(expected,r.StatusCode);Assert.Equal("no-store",r.Headers.CacheControl?.ToString());return(await r.Content.ReadFromJsonAsync<T>())!;}}
    private static async Task Fault(HttpResponseMessage r,HttpStatusCode status,string? code=null)
    {using(r){Assert.Equal(status,r.StatusCode);if(code!=null)Assert.Contains($"\"code\":\"{code}\"",await r.Content.ReadAsStringAsync());}}
    private static async Task<CustomerBookingView> Book(HttpClient c,Data d,DateTimeOffset start)=>await Json<CustomerBookingView>(await c.PostAsJsonAsync(d.Root+"/appointments",d.Input(start)),HttpStatusCode.Created);

    [PostgresFact]
    public async Task DiscoverBookAndOwnerAgendaKeepOwnershipSeparateAndSafeReplayReturnsCurrentState()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var c=Client(api,d.Customer);using var owner=Client(api,d.Owner);
        var businesses=await Json<DiscoveryBusiness[]>(await c.GetAsync("/api/v1/customer/businesses?q="+Uri.EscapeDataString("Müşteri randevusu")));Assert.Contains(businesses,x=>x.Id==d.Business);
        var detail=await Json<CustomerBusinessDetail>(await c.GetAsync(d.Root));Assert.Single(detail.Services);Assert.Equal(120,detail.CancellationRescheduleCutoffMinutes);Assert.Equal("450.25",detail.Services[0].Offerings[0].Price);
        var start=clock.Now.AddHours(3);var slots=await Json<CustomerSlotsView>(await c.GetAsync(d.Root+$"/services/{d.Service}/vehicles/{d.Vehicle}/slots?date={start:yyyy-MM-dd}&stepMinutes=1"));Assert.NotEmpty(slots.Items);Assert.Equal(d.VehicleVersion,slots.VehicleVersion);
        var input=d.Input(start);var booked=await Json<CustomerBookingView>(await c.PostAsJsonAsync(d.Root+"/appointments",input),HttpStatusCode.Created);
        Assert.True(booked.CanChange);Assert.Equal("Confirmed",booked.Status);Assert.Equal("450.25",booked.Price);
        var agenda=await Json<AppointmentView>(await owner.GetAsync($"/api/v1/businesses/{d.Business}/appointments/{booked.Id}"));Assert.Equal(BookingSource.Customer,agenda.Source);Assert.Null(agenda.LocalCustomerId);Assert.Null(agenda.LocalVehicleId);
        var cancelled=await Json<CustomerBookingView>(await c.PostAsJsonAsync($"/api/v1/customer/appointments/{booked.Id}/cancel",new CustomerChangeInput(Guid.NewGuid(),booked.Version)));Assert.Equal("Cancelled",cancelled.Status);
        var replay=await Json<CustomerBookingView>(await c.PostAsJsonAsync(d.Root+"/appointments",input));Assert.Equal(booked.Id,replay.Id);Assert.Equal("Cancelled",replay.Status);
        await using var db=Db();var row=await db.Appointments.SingleAsync(x=>x.Id==booked.Id);Assert.Equal(d.Profile,row.CustomerProfileId);Assert.Equal(d.Vehicle,row.OwnedVehicleId);Assert.Null(row.LocalCustomerId);Assert.Null(row.LocalVehicleId);
        Assert.Equal(1,await db.LocalCustomers.CountAsync(x=>x.BusinessId==d.Business));Assert.All(await db.AppointmentResourceReservations.Where(x=>x.AppointmentId==row.Id).ToListAsync(),x=>Assert.NotNull(x.ReleasedAt));
    }
    [PostgresFact]
    public async Task ForeignOwnershipHiddenServiceAndRevokedSessionAreRejected()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var c=Client(api,d.Customer);using var stranger=Client(api,d.Stranger);using var anonymous=api.CreateClient();
        await Fault(await anonymous.GetAsync(d.Root),HttpStatusCode.Unauthorized);
        await Fault(await stranger.PostAsJsonAsync(d.Root+"/appointments",d.Input(clock.Now.AddHours(3))),HttpStatusCode.NotFound);
        await Fault(await c.PostAsJsonAsync(d.Root+"/appointments",d.Input(clock.Now.AddHours(3)) with{ServiceId=d.Hidden}),HttpStatusCode.NotFound);
        var booked=await Book(c,d,clock.Now.AddHours(3));
        await Fault(await stranger.GetAsync($"/api/v1/customer/appointments/{booked.Id}"),HttpStatusCode.NotFound);
        await Fault(await stranger.PostAsJsonAsync($"/api/v1/customer/appointments/{booked.Id}/cancel",new CustomerChangeInput(Guid.NewGuid(),booked.Version)),HttpStatusCode.NotFound);
        Assert.Empty(await Json<CustomerBookingView[]>(await stranger.GetAsync("/api/v1/customer/appointments")));
        await using(var db=Db())await db.DeviceSessions.Where(x=>x.Id==d.Customer.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
        await Fault(await c.GetAsync("/api/v1/customer/appointments"),HttpStatusCode.Unauthorized);
    }
    [PostgresFact]
    public async Task ExactTwoHourBoundaryAllowsChangeAndOneTickLaterPreservesConfirmedAppointment()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var c=Client(api,d.Customer);
        var first=await Book(c,d,clock.Now.AddHours(2));
        var moved=await Json<CustomerBookingView>(await c.PostAsJsonAsync($"/api/v1/customer/appointments/{first.Id}/reschedule",new CustomerRescheduleInput(Guid.NewGuid(),first.Version,clock.Now.AddHours(4))));Assert.NotEqual(first.StartsAtUtc,moved.StartsAtUtc);
        var second=await Book(c,d,clock.Now.AddHours(2));clock.Now=clock.Now.AddTicks(1);
        await Fault(await c.PostAsJsonAsync($"/api/v1/customer/appointments/{second.Id}/cancel",new CustomerChangeInput(Guid.NewGuid(),second.Version)),HttpStatusCode.Conflict,"bookingChangeCutoff");
        await Fault(await c.PostAsJsonAsync($"/api/v1/customer/appointments/{second.Id}/reschedule",new CustomerRescheduleInput(Guid.NewGuid(),second.Version,second.StartsAtUtc.AddHours(3))),HttpStatusCode.Conflict,"bookingChangeCutoff");
        var intact=await Json<CustomerBookingView>(await c.GetAsync($"/api/v1/customer/appointments/{second.Id}"));Assert.Equal(second.Version,intact.Version);Assert.Equal(second.StartsAtUtc,intact.StartsAtUtc);Assert.False(intact.CanChange);
    }
    [PostgresFact]
    public async Task ConcurrentOwnerAndCustomerCannotDoubleBookEmployeeEvenWithSpareAreaCapacity()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var c=Client(api,d.Customer);using var owner=Client(api,d.Owner);var start=clock.Now.AddHours(3);uint localVersion;
        await using(var db=Db()){var v=await db.LocalVehicles.SingleAsync(x=>x.Id==d.LocalVehicle);localVersion=db.Entry(v).Property<uint>("Version").CurrentValue;}
        var responses=await Task.WhenAll(c.PostAsJsonAsync(d.Root+"/appointments",d.Input(start)),owner.PostAsJsonAsync($"/api/v1/businesses/{d.Business}/appointments",new BookingInput(Guid.NewGuid(),d.LocalCustomer,d.LocalVehicle,d.Service,d.ServiceVersion,localVersion,start.ToString("yyyy-MM-dd'T'HH:mm"),null,[d.Employee])));
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);foreach(var response in responses)response.Dispose();
        await using var verify=Db();Assert.Equal(1,await verify.Appointments.CountAsync(x=>x.BusinessId==d.Business&&x.Status==AppointmentStatus.Confirmed));
    }
    [PostgresFact]
    public async Task FailedMoveKeepsOriginalReservationsAndPreparedWorkThenExplicitMoveRetiresOnlyUnstartedWork()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var c=Client(api,d.Customer);using var owner=Client(api,d.Owner);
        var first=await Book(c,d,clock.Now.AddHours(3));var occupied=await Book(c,d,clock.Now.AddHours(4));
        var work=await Json<WorkOrderView>(await owner.PostAsJsonAsync($"/api/v1/businesses/{d.Business}/appointments/{first.Id}/work-order",new CreateWorkOrderInput(Guid.NewGuid(),first.Version,[new("Yıkama",30)])));
        await Fault(await c.PostAsJsonAsync($"/api/v1/customer/appointments/{first.Id}/reschedule",new CustomerRescheduleInput(Guid.NewGuid(),first.Version,occupied.StartsAtUtc)),HttpStatusCode.Conflict,"resourceConflict");
        var intact=await Json<CustomerBookingView>(await c.GetAsync($"/api/v1/customer/appointments/{first.Id}"));Assert.Equal(first.StartsAtUtc,intact.StartsAtUtc);Assert.Equal(first.Version,intact.Version);
        var before=await Json<WorkOrderView>(await owner.GetAsync($"/api/v1/businesses/{d.Business}/owner/work-orders/{work.Id}"));Assert.Equal("Scheduled",before.Status);
        var moved=await Json<CustomerBookingView>(await c.PostAsJsonAsync($"/api/v1/customer/appointments/{first.Id}/reschedule",new CustomerRescheduleInput(Guid.NewGuid(),first.Version,clock.Now.AddHours(5))));Assert.Equal("NotStarted",moved.WorkStatus);
        var history=await Json<WorkOrderView>(await owner.GetAsync($"/api/v1/businesses/{d.Business}/owner/work-orders/{work.Id}"));Assert.Equal("Cancelled",history.Status);Assert.Equal(first.StartsAtUtc,history.PlannedStartUtc);
    }
    [PostgresFact]
    public async Task BusinessSpecificCutoffAndUnconfiguredHoursFailClosed()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var c=Client(api,d.Customer);
        var a=await Book(c,d,clock.Now.AddHours(3));
        await using(var db=Db())await db.Businesses.Where(x=>x.Id==d.Business).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CancellationRescheduleCutoffMinutes,240));
        await Fault(await c.PostAsJsonAsync($"/api/v1/customer/appointments/{a.Id}/cancel",new CustomerChangeInput(Guid.NewGuid(),a.Version)),HttpStatusCode.Conflict,"bookingChangeCutoff");
        await using(var db=Db())await db.Businesses.Where(x=>x.Id==d.Business).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.HoursConfigured,false));
        await Fault(await c.PostAsJsonAsync(d.Root+"/appointments",d.Input(clock.Now.AddHours(5))),HttpStatusCode.Conflict,"hoursUnconfigured");
        var saved=await Json<CustomerBookingView>(await c.GetAsync($"/api/v1/customer/appointments/{a.Id}"));Assert.Equal("Confirmed",saved.Status);Assert.Equal(a.StartsAtUtc,saved.StartsAtUtc);
    }
    [PostgresFact]
    public async Task UpgradeFromPopulatedStageSchemaPreservesOwnerAppointmentAndCustomizedPolicyBlocksDowngrade()
    {
        var isolated=new PostgresModelFixture();await isolated.InitializeAsync();
        try
        {
            var test=new CustomerBookingHttpPostgresTests(isolated);var clock=new Clock();var d=await test.Seed(clock);
            await using var db=test.Db();var local=await db.LocalVehicles.SingleAsync(x=>x.Id==d.LocalVehicle);var version=db.Entry(local).Property<uint>("Version").CurrentValue;db.ChangeTracker.Clear();
            var service=new EfSchedulingService(db,clock);var created=await service.Create(new(d.Owner.UserId,d.Owner.Id),d.Business,new BookingInput(Guid.NewGuid(),d.LocalCustomer,d.LocalVehicle,d.Service,d.ServiceVersion,version,clock.Now.AddHours(3).ToString("yyyy-MM-dd'T'HH:mm"),null,[d.Employee]));
            await db.GetService<IMigrator>().MigrateAsync("20261125090139_OrderedStageScheduling");
            await db.Database.MigrateAsync();
            var row=await db.Appointments.SingleAsync(x=>x.Id==created.Value.Id);Assert.Equal(created.Value.StartsAtUtc,row.StartsAtUtc);Assert.Equal(AppointmentStatus.Confirmed,row.Status);Assert.Equal(d.LocalCustomer,row.LocalCustomerId);Assert.Equal(d.LocalVehicle,row.LocalVehicleId);Assert.Null(row.CustomerProfileId);Assert.Equal(2,await db.AppointmentResourceReservations.CountAsync(x=>x.AppointmentId==row.Id&&x.ReleasedAt==null));
            var business=await db.Businesses.SingleAsync(x=>x.Id==d.Business);Assert.Equal(120,business.CancellationRescheduleCutoffMinutes);business.CancellationRescheduleCutoffMinutes=90;await db.SaveChangesAsync();
            var error=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync("20261125090139_OrderedStageScheduling"));Assert.Contains("data-preserving resolution",error.MessageText);
            db.ChangeTracker.Clear();Assert.Equal(90,(await db.Businesses.SingleAsync(x=>x.Id==d.Business)).CancellationRescheduleCutoffMinutes);Assert.Equal(AppointmentStatus.Confirmed,(await db.Appointments.SingleAsync(x=>x.Id==row.Id)).Status);
        }
        finally{await isolated.DisposeAsync();}
    }

    [PostgresFact]
    public async Task SameCustomerBooksOwnedVehicleAcrossBusinessesWithoutLeakingEitherHistory()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var first=await Seed(clock);var second=await Seed(clock);
        using var customer=Client(api,first.Customer);using var otherCustomer=Client(api,second.Customer);using var firstOwner=Client(api,first.Owner);using var secondOwner=Client(api,second.Owner);
        var a=await Book(customer,first,clock.Now.AddHours(3));
        var b=await Json<CustomerBookingView>(await customer.PostAsJsonAsync(second.Root+"/appointments",second.Input(clock.Now.AddHours(4)) with{OwnedVehicleId=first.Vehicle,ExpectedVehicleVersion=first.VehicleVersion}),HttpStatusCode.Created);
        var mine=await Json<CustomerBookingView[]>(await customer.GetAsync("/api/v1/customer/appointments"));Assert.Equal(2,mine.Length);Assert.Contains(mine,x=>x.BusinessId==first.Business);Assert.Contains(mine,x=>x.BusinessId==second.Business);
        Assert.Empty(await Json<CustomerBookingView[]>(await otherCustomer.GetAsync("/api/v1/customer/appointments")));
        await Fault(await otherCustomer.GetAsync($"/api/v1/customer/appointments/{b.Id}"),HttpStatusCode.NotFound);
        await Fault(await firstOwner.GetAsync($"/api/v1/businesses/{second.Business}/appointments/{b.Id}"),HttpStatusCode.Forbidden);
        await Fault(await secondOwner.GetAsync($"/api/v1/businesses/{first.Business}/appointments/{a.Id}"),HttpStatusCode.Forbidden);
        await Json<AppointmentView>(await firstOwner.GetAsync($"/api/v1/businesses/{first.Business}/appointments/{a.Id}"));await Json<AppointmentView>(await secondOwner.GetAsync($"/api/v1/businesses/{second.Business}/appointments/{b.Id}"));
        await using var db=Db();var rows=await db.Appointments.Where(x=>x.CustomerProfileId==first.Profile).ToListAsync();Assert.Equal(2,rows.Count);Assert.All(rows,x=>{Assert.Equal(first.Vehicle,x.OwnedVehicleId);Assert.Null(x.LocalCustomerId);Assert.Null(x.LocalVehicleId);});
    }

}
