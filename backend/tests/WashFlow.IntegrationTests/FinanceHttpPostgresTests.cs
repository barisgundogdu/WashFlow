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
using WashFlow.Application.Finance;
using WashFlow.Infrastructure.Finance;
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
public sealed class FinanceHttpPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
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
        s.RemoveAll<IFinanceService>();s.AddScoped<IFinanceService>(sp=>new EfFinanceService(sp.GetRequiredService<WashFlowDbContext>(),clock));
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
    private static async Task<WorkOrderView> Complete(HttpClient owner,HttpClient employee,Data d,Clock clock)
    {
        var order=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input()));
        foreach(var i in new[]{0,1}){
            order=await Json<WorkOrderView>(await employee.PostAsJsonAsync($"{d.EmployeePath}/{order.Id}/stages/{order.Stages[i].Id}/start",new WorkActionInput(Guid.NewGuid(),order.Version)));
            clock.Now=clock.Now.AddMinutes(i==0?10:20);
            order=await Json<WorkOrderView>(await employee.PostAsJsonAsync($"{d.EmployeePath}/{order.Id}/stages/{order.Stages[i].Id}/complete",new WorkActionInput(Guid.NewGuid(),order.Version)));
        }
        Assert.Equal("Completed",order.Status);return order;
    }
    private static FinanceEntryInput Pay(Clock clock,Guid work,Guid method,string amount="150.10")=>new(Guid.NewGuid(),amount,clock.Now,WorkOrderId:work,PaymentMethodId:method);
    [PostgresFact]
    public async Task CompletedWashPartialPaymentsExpenseAndReportDoNotDoubleCountRevenue()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var owner=Client(api,d.OwnerSession);using var employee=Client(api,d.EmployeeSession);var work=await Complete(owner,employee,d,clock);var path=d.Root+"/finance";
        var settings=await Json<FinanceSettings>(await owner.GetAsync(path+"/settings"));Assert.Equal(new[]{"Banka havalesi","Kart","Nakit"},settings.Methods.Select(x=>x.Name));
        var method=settings.Methods.Single(x=>x.Name=="Nakit");var input=Pay(clock,work.Id,method.Id);var paid=await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/payments",input));Assert.Equal(paid.Id,(await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/payments",input))).Id);
        var receivable=Assert.Single(await Json<ReceivableView[]>(await owner.GetAsync(path+"/receivables")));Assert.Equal("Partial",receivable.Status);Assert.Equal("299.90",receivable.Remaining);
        await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/payments",Pay(clock,work.Id,method.Id,"299.90")));
        Assert.Empty(await Json<ReceivableView[]>(await owner.GetAsync(path+"/receivables")));
        await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/manual-income",new FinanceEntryInput(Guid.NewGuid(),"50.25",clock.Now,Category:"Ek hizmet",PaymentMethodId:method.Id)));
        await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/expenses",new FinanceEntryInput(Guid.NewGuid(),"100.15",clock.Now,Category:"Temizlik malzemesi",Description:"Deterjan",Reference:"Fatura 1",EmployeeMembershipId:d.Employee)));
        var report=await Json<FinanceReport>(await owner.GetAsync(path+"/report"));Assert.Equal("450.00",report.EarnedRevenue);Assert.Equal("450.00",report.CollectedPayments);Assert.Equal("50.25",report.ManualIncome);Assert.Equal("100.15",report.Expenses);Assert.Equal("400.10",report.CashOperatingBalance);Assert.Equal("400.10",report.ServiceOperatingResult);Assert.Equal("0.00",report.UnpaidBalance);Assert.Equal("500.25",Assert.Single(report.PaymentMethods).Amount);Assert.Equal("450.00",Assert.Single(report.ServiceRevenue).Amount);
    }
    [PostgresFact]
    public async Task ConcurrentPaymentsCannotOverpayAndPriceChangesDoNotRewriteAmounts()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var owner=Client(api,d.OwnerSession);using var employee=Client(api,d.EmployeeSession);var work=await Complete(owner,employee,d,clock);var path=d.Root+"/finance";
        var method=(await Json<FinanceSettings>(await owner.GetAsync(path+"/settings"))).Methods[0];var input=Pay(clock,work.Id,method.Id,"300.00");
        var responses=await Task.WhenAll(owner.PostAsJsonAsync(path+"/entries/payments",input),owner.PostAsJsonAsync(path+"/entries/payments",input with{RequestId=Guid.NewGuid()}));Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);foreach(var response in responses)response.Dispose();
        await Fault(await owner.PostAsJsonAsync(path+"/entries/payments",input with{Amount="301.00"}),HttpStatusCode.Conflict);
        await using(var db=Db()){var id=await db.Appointments.Where(x=>x.Id==d.Appointment).Select(x=>x.ServiceId).SingleAsync();await db.ServiceOfferings.Where(x=>x.ServiceId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Price,999m));}
        var report=await Json<FinanceReport>(await owner.GetAsync(path+"/report"));Assert.Equal("450.00",report.EarnedRevenue);Assert.Equal("300.00",report.CollectedPayments);Assert.Equal("150.00",report.UnpaidBalance);Assert.Equal("300.00",Assert.Single(await Json<FinanceEntryView[]>(await owner.GetAsync(path+"/entries/payments"))).Amount);
    }
    [PostgresFact]
    public async Task OnlyCurrentOwnerCanReadOrWriteAndForeignRelationshipsAreRejected()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);var other=await Seed(clock);using var owner=Client(api,d.OwnerSession);using var employee=Client(api,d.EmployeeSession);using var foreign=Client(api,other.OwnerSession);using var anonymous=api.CreateClient();var path=d.Root+"/finance";
        foreach(var endpoint in new[]{"/settings","/receivables","/report","/entries/expenses","/entries/payments"}){await Fault(await employee.GetAsync(path+endpoint),HttpStatusCode.Forbidden);await Fault(await foreign.GetAsync(path+endpoint),HttpStatusCode.Forbidden);await Fault(await anonymous.GetAsync(path+endpoint),HttpStatusCode.Unauthorized);}
        var settings=await Json<FinanceSettings>(await owner.GetAsync(path+"/settings"));var input=new FinanceEntryInput(Guid.NewGuid(),"10.00",clock.Now,Category:"Kira");
        await Fault(await employee.PostAsJsonAsync(path+"/entries/expenses",input),HttpStatusCode.Forbidden);
        await Fault(await owner.PostAsJsonAsync(path+"/entries/expenses",input with{EmployeeMembershipId=other.Employee}),HttpStatusCode.NotFound);
        await Fault(await owner.PostAsJsonAsync(path+"/entries/payments",Pay(clock,Guid.NewGuid(),settings.Methods[0].Id)),HttpStatusCode.NotFound);
        var scheduled=await Json<WorkOrderView>(await owner.PostAsJsonAsync(d.Create,d.Input()));await Fault(await owner.PostAsJsonAsync(path+"/entries/payments",Pay(clock,scheduled.Id,settings.Methods[0].Id)),HttpStatusCode.Conflict,"workNotCompleted");
        await using(var db=Db())await db.BusinessMemberships.Where(x=>x.BusinessId==d.Business&&x.UserId==d.OwnerSession.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
        await Fault(await owner.GetAsync(path+"/report"),HttpStatusCode.Forbidden);
    }
    [PostgresFact]
    public async Task CorrectionsPreserveImmutableHistoryAndAtomicExpenseReplacementIsRetrySafe()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var owner=Client(api,d.OwnerSession);var path=d.Root+"/finance";
        var original=await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/expenses",new FinanceEntryInput(Guid.NewGuid(),"100.00",clock.Now,Category:"Kira")));
        var correction=new FinanceEntryInput(Guid.NewGuid(),"80.00",clock.Now,Category:"Kira",CorrectsId:original.Id,CorrectionReason:"Hatalı tutar");
        var replacement=await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/expenses",correction));Assert.Equal(original.Id,replacement.CorrectsId);Assert.Equal(replacement.Id,(await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/expenses",correction))).Id);
        var entries=await Json<FinanceEntryView[]>(await owner.GetAsync(path+"/entries/expenses"));Assert.Equal(3,entries.Length);Assert.Single(entries,x=>x.ReversesId==original.Id);Assert.Contains(entries,x=>x.Id==original.Id&&x.Amount=="100.00");
        var report=await Json<FinanceReport>(await owner.GetAsync(path+"/report"));Assert.Equal("80.00",report.Expenses);
        var reverse=new ReverseEntryInput(Guid.NewGuid(),"Mükerrer kayıt");var reversed=await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+$"/entries/expenses/{replacement.Id}/reverse",reverse));Assert.Equal(reversed.Id,(await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+$"/entries/expenses/{replacement.Id}/reverse",reverse))).Id);
        await Fault(await owner.PostAsJsonAsync(path+$"/entries/expenses/{replacement.Id}/reverse",reverse with{RequestId=Guid.NewGuid()}),HttpStatusCode.Conflict,"entryReversed");
        report=await Json<FinanceReport>(await owner.GetAsync(path+"/report"));Assert.Equal("0.00",report.Expenses);
        await using var verify=Db();var error=await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Expenses\" SET \"Amount\" = 1 WHERE \"Id\" = {original.Id}"));Assert.Equal(PostgresErrorCodes.CheckViolation,error.SqlState);
    }
    [PostgresFact]
    public async Task LocalDateRangesAreHalfOpenAndMalformedAmountsAndArchivedMethodsFailClosed()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var owner=Client(api,d.OwnerSession);var path=d.Root+"/finance";
        await using(var db=Db())await db.Businesses.Where(x=>x.Id==d.Business).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TimeZoneId,"Europe/Istanbul"));
        var today=DateOnly.FromDateTime(clock.Now.UtcDateTime);var midnight=new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(3)).ToUniversalTime();
        foreach(var tuple in new[]{(At:midnight.AddTicks(-1),Amount:"1.00"),(At:midnight,Amount:"2.00")})await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/expenses",new FinanceEntryInput(Guid.NewGuid(),tuple.Amount,tuple.At,Category:"Su")));
        var report=await Json<FinanceReport>(await owner.GetAsync(path+$"/report?fromDate={today:yyyy-MM-dd}&toDateExclusive={today.AddDays(1):yyyy-MM-dd}"));Assert.Equal("2.00",report.Expenses);Assert.Single(report.Trends);Assert.Equal("Europe/Istanbul",report.TimeZoneId);
        var previous=await Json<FinanceReport>(await owner.GetAsync(path+$"/report?fromDate={today.AddDays(-1):yyyy-MM-dd}&toDateExclusive={today:yyyy-MM-dd}"));Assert.Equal("1.00",previous.Expenses);
        foreach(var amount in new[]{"0","-1","1.001","1.000","01","NaN","1e3","10000000.00","1,50"})await Fault(await owner.PostAsJsonAsync(path+"/entries/expenses",new FinanceEntryInput(Guid.NewGuid(),amount,clock.Now,Category:"Su")),HttpStatusCode.BadRequest,"validation");
        var settings=await Json<FinanceSettings>(await owner.GetAsync(path+"/settings"));var method=await Json<PaymentMethodView>(await owner.PostAsJsonAsync(path+"/methods",new MethodInput("Özel yöntem")));Assert.Equal(method.Id,(await Json<PaymentMethodView>(await owner.PostAsJsonAsync(path+"/methods",new MethodInput("Özel yöntem")))).Id);
        await Json<PaymentMethodView>(await owner.PostAsJsonAsync(path+$"/methods/{method.Id}/archive",new VersionInput(method.Version)));
        await Fault(await owner.PostAsJsonAsync(path+"/entries/manual-income",new FinanceEntryInput(Guid.NewGuid(),"10.00",clock.Now,Category:"Diğer",PaymentMethodId:method.Id)),HttpStatusCode.NotFound);
    }
    [PostgresFact]
    public async Task PaymentReversalRestoresCollectibleBalanceWithoutChangingEarnedRevenue()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var owner=Client(api,d.OwnerSession);using var employee=Client(api,d.EmployeeSession);var work=await Complete(owner,employee,d,clock);var path=d.Root+"/finance";
        var method=(await Json<FinanceSettings>(await owner.GetAsync(path+"/settings"))).Methods[0];var original=await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/payments",Pay(clock,work.Id,method.Id,"300.00")));
        await Fault(await employee.PostAsJsonAsync(path+$"/entries/payments/{original.Id}/reverse",new ReverseEntryInput(Guid.NewGuid(),"Hata")),HttpStatusCode.Forbidden);
        await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+$"/entries/payments/{original.Id}/reverse",new ReverseEntryInput(Guid.NewGuid(),"Yanlış ödeme kaydı")));
        var due=Assert.Single(await Json<ReceivableView[]>(await owner.GetAsync(path+"/receivables")));Assert.Equal("450.00",due.Remaining);
        await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/payments",Pay(clock,work.Id,method.Id,"450.00")));
        await Json<PaymentMethodView>(await owner.PostAsJsonAsync(path+$"/methods/{method.Id}/archive",new VersionInput(method.Version)));
        var report=await Json<FinanceReport>(await owner.GetAsync(path+"/report"));Assert.Equal("450.00",report.EarnedRevenue);Assert.Equal("450.00",report.CollectedPayments);Assert.Equal("0.00",report.UnpaidBalance);Assert.Equal(method.Name,Assert.Single(report.PaymentMethods).Label);
    }
    [PostgresFact]
    public async Task FailedExpenseReplacementRollsBackItsAlreadyInsertedReversal()
    {
        var clock=new Clock();using var root=new ApiFactory();using var api=Api(root,clock);var d=await Seed(clock);using var owner=Client(api,d.OwnerSession);var path=d.Root+"/finance";
        var original=await Json<FinanceEntryView>(await owner.PostAsJsonAsync(path+"/entries/expenses",new FinanceEntryInput(Guid.NewGuid(),"90.00",clock.Now,Category:"Su")));
        await using(var db=Db())await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_finance_replacement() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"CorrectsId\" IS NOT NULL THEN RAISE EXCEPTION 'injected failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER reject_finance_replacement BEFORE INSERT ON \"Expenses\" FOR EACH ROW EXECUTE FUNCTION reject_finance_replacement()");
        try{await Fault(await owner.PostAsJsonAsync(path+"/entries/expenses",new FinanceEntryInput(Guid.NewGuid(),"80.00",clock.Now,Category:"Su",CorrectsId:original.Id,CorrectionReason:"Düzeltme")),HttpStatusCode.InternalServerError);}
        finally{await using var db=Db();await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_finance_replacement ON \"Expenses\"; DROP FUNCTION reject_finance_replacement()");}
        var retained=Assert.Single(await Json<FinanceEntryView[]>(await owner.GetAsync(path+"/entries/expenses")));Assert.Equal(original.Id,retained.Id);Assert.Equal("90.00",retained.Amount);
    }
    [PostgresFact]
    public async Task AdditiveFinanceUpgradePreservesExistingAppointmentAndRefusesLossyDowngrade()
    {
        var isolated=new PostgresModelFixture();await isolated.InitializeAsync();
        try
        {
            var test=new FinanceHttpPostgresTests(isolated);var clock=new Clock();var d=await test.Seed(clock);await using var db=test.Db();
            await db.GetService<IMigrator>().MigrateAsync("20261125130449_CustomerSelfServiceBooking");await db.Database.MigrateAsync();
            var appointment=await db.Appointments.SingleAsync(x=>x.Id==d.Appointment);Assert.Equal(d.Version,db.Entry(appointment).Property<uint>("Version").CurrentValue);Assert.Equal(AppointmentStatus.Confirmed,appointment.Status);Assert.Empty(await db.Payments.ToListAsync());Assert.Empty(await db.Expenses.ToListAsync());Assert.Empty(await db.ManualIncomes.ToListAsync());
            db.ChangeTracker.Clear();var service=new EfFinanceService(db,clock);await service.Record(new(d.OwnerSession.UserId,d.OwnerSession.Id),d.Business,"expenses",new(Guid.NewGuid(),"1.00",clock.Now,Category:"Diğer"));
            var error=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync("20261125130449_CustomerSelfServiceBooking"));Assert.Contains("data-preserving resolution",error.MessageText);Assert.Single(await db.Expenses.ToListAsync());
        }
        finally{await isolated.DisposeAsync();}
    }

}
