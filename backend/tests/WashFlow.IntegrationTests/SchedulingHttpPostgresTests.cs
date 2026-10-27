using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WashFlow.Application.Authentication;
using WashFlow.Application.Authorization;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Domain.Scheduling;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class SchedulingHttpPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private static readonly DateTimeOffset Day = new(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private WashFlowDbContext Db() => new(fixture.Options);
    private WebApplicationFactory<Program> Api(ApiFactory root, DbCommandInterceptor? interceptor = null, Action<IServiceCollection>? configure = null) => root.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
        s.RemoveAll<WashFlowDbContext>();
        s.AddScoped(_ => new WashFlowDbContext(interceptor == null ? fixture.Options : new DbContextOptionsBuilder<WashFlowDbContext>(fixture.Options).AddInterceptors(interceptor).Options));
        configure?.Invoke(s);
    }));
    private sealed record Data(Guid Business, Guid User, Guid Membership, Guid Session, Guid Service, Guid Customer, Guid Vehicle,
        Guid Area, Guid Requirement, Guid[] Employees, Guid[] Windows, uint ServiceVersion, uint VehicleVersion, uint AreaVersion, uint RequirementVersion, uint WindowVersion)
    {
        public string Root => $"/api/v1/businesses/{Business}";
        public string Appointments => Root + "/appointments";
        public string RequirementPath => $"{Root}/services/{Service}/offerings/Car/scheduling-requirement";
        public string AvailabilityPath => $"{Root}/employees/{Employees[0]}/availability";
        public BookingInput Booking(string time = "2030-01-02T10:00", int employee = 0) => new(Guid.NewGuid(), Customer, Vehicle, Service, ServiceVersion, VehicleVersion, time, null, [Employees[employee]]);
    }
    private async Task<Data> Seed(WebApplicationFactory<Program> api, HttpClient client, MembershipRole? role = MembershipRole.Owner, int capacity = 1, string zone = "Etc/UTC", bool allDay = false)
    {
        await using var db = Db();
        Assert.Equal(17, (await db.Database.SqlQueryRaw<int>("SELECT current_setting('server_version_num')::int AS \"Value\"").SingleAsync()) / 10000);
        var now = DateTimeOffset.UtcNow;
        var u = new User("Sahip", $"{Guid.NewGuid():N}@example.test", "hash") { EmailVerifiedAt = DateTimeOffset.UtcNow };
        var b = new Business { Name = "Planlama", TimeZoneId = zone };
        var m = new BusinessMembership { BusinessId = b.Id, UserId = u.Id };
        var session = new DeviceSession { UserId = u.Id, SecurityStamp = u.SecurityStamp, DeviceName = "Test", ExpiresAt = now.AddHours(1) };
        db.AddRange(u, b, session);
        if (role != null) db.AddRange(m, new BusinessMembershipRole { BusinessMembershipId = m.Id, Role = role.Value });
        else db.Add(new CustomerProfile { UserId = u.Id });
        var c = new LocalCustomer { BusinessId = b.Id, FullName = "Müşteri", NormalizedName = "MÜŞTERİ" };
        var v = new LocalVehicle { BusinessId = b.Id, LocalCustomerId = c.Id, Plate = "34 TEST 1", NormalizedPlate = "34TEST1", Brand = "Test", Model = "Test", Category = "Car" };
        var service = new BusinessService { BusinessId = b.Id, Name = "Yıkama", SearchName = "YIKAMA", Enabled = true };
        var area = WorkArea.Create(b.Id, "Alan", capacity, now);
        var requirement = ServiceSchedulingRequirement.Create(b.Id, service.Id, "Car", area.Id, true, now);
        db.AddRange(c, v, service, area, requirement,
            new ServiceOffering { ServiceId = service.Id, Category = "Car", Price = 450.10m, Currency = "TRY", ActiveMinutes = 30, WaitingMinutes = 60, Employees = 1 });
        for (var day = 1; day <= 7; day++) db.Add(new WeeklyOpeningDay { BusinessId = b.Id, Day = day, OpensAtMinute = allDay ? 0 : 480, ClosesAtMinute = allDay ? 1440 : 1200 });
        var employees = new List<Guid>(); var windows = new List<EmployeeAvailability>();
        for (var i = 0; i < 3; i++)
        {
            var employee = new User("Çalışan", $"{Guid.NewGuid():N}@example.test", "hash");
            var member = new BusinessMembership { BusinessId = b.Id, UserId = employee.Id };
            var window = EmployeeAvailability.Create(b.Id, member.Id, new(Day.AddDays(-1), Day.AddDays(2)), now);
            db.AddRange(employee, member, new BusinessMembershipRole { BusinessMembershipId = member.Id, Role = MembershipRole.Employee }, window);
            employees.Add(member.Id); windows.Add(window);
        }
        await db.SaveChangesAsync();
        uint Version<T>(T entity) where T : class => db.Entry(entity).Property<uint>("Version").CurrentValue;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(session).Token);
        return new(b.Id, u.Id, m.Id, session.Id, service.Id, c.Id, v.Id, area.Id, requirement.Id, employees.ToArray(), windows.Select(x => x.Id).ToArray(),
            Version(service), Version(v), Version(area), Version(requirement), Version(windows[0]));
    }
    private static async Task<T> Json<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        using (response) { Assert.Equal(status, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl?.ToString()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }
    private static async Task Problem(HttpResponseMessage response, int status, string code, params string[] forbidden)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text);
            Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32()); Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
            Assert.False(json.RootElement.TryGetProperty("detail", out _)); Assert.False(json.RootElement.TryGetProperty("instance", out _));
            foreach (var value in forbidden.Concat(new[] { "Password", "Npgsql", "stackTrace", "SELECT", "SENSITIVE" })) Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }
    private static async Task<AppointmentView> Book(HttpClient client, Data d, BookingInput? input = null) =>
        await Json<AppointmentView>(await client.PostAsJsonAsync(d.Appointments, input ?? d.Booking()), HttpStatusCode.Created);

    [PostgresFact]
    public async Task OwnerLifecycleCalendarPreviewAndRetainedHistoryUseExistingEngine()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var client = api.CreateClient(); var d = await Seed(api, client);
        var preview = new AvailabilityCheckInput(d.Service, "Car", "2030-01-02T10:00", null, [d.Employees[0]]);
        Assert.True((await Json<AvailabilityCheckView>(await client.PostAsJsonAsync(d.Appointments + "/availability-preview", preview))).Available);
        var original = await Book(client, d); Assert.Equal("450.10", original.Price); Assert.Equal(30, original.ActiveMinutes); Assert.Equal(60, original.WaitingMinutes);
        Assert.Equal(original.StartsAtUtc.AddMinutes(30), original.ActiveEndsAtUtc); Assert.Equal(2, original.Reservations.Count); Assert.Single(original.Events);
        Assert.False((await Json<AvailabilityCheckView>(await client.PostAsJsonAsync(d.Appointments + "/availability-preview", preview))).Available);
        var read = await Json<AppointmentView>(await client.GetAsync(d.Appointments + "/" + original.Id)); Assert.Equal(original.Version, read.Version);
        var calendar = await Json<AppointmentView[]>(await client.GetAsync(d.Appointments + "?fromDate=2030-01-02&toDateExclusive=2030-01-03&pageSize=1")); Assert.Single(calendar);
        var moved = await Json<AppointmentView>(await client.PostAsJsonAsync($"{d.Appointments}/{original.Id}/reschedule", new RescheduleInput(original.Version, "2030-01-02T11:00", null, [d.Employees[1]])));
        Assert.NotEqual(original.Version, moved.Version); Assert.Equal(2, moved.Events.Count); Assert.Equal(2, moved.Reservations.Count(x => x.ReleasedAt != null));
        var cancelled = await Json<AppointmentView>(await client.PostAsJsonAsync($"{d.Appointments}/{original.Id}/cancel", new VersionInput(moved.Version)));
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status); Assert.All(cancelled.Reservations, x => Assert.NotNull(x.ReleasedAt));
        var events = await Json<EventView[]>(await client.GetAsync($"{d.Appointments}/{original.Id}/events"));
        Assert.Equal(new[] { AppointmentEventKind.Created, AppointmentEventKind.Rescheduled, AppointmentEventKind.Cancelled }, events.Select(x => x.Kind));
        Assert.Equal(new[] { 1, 2, 3 }, events.Select(x => x.Sequence));
        Assert.Single(await Json<AppointmentView[]>(await client.GetAsync(d.Appointments + "?fromDate=2030-01-02&toDateExclusive=2030-01-03&status=Cancelled")));
        await Book(client, d); // Cancellation released the original slot too.
    }
    [PostgresFact]
    public async Task WorkAreaCrudVersionsAndCapacityArchiveGuardsAreExposed()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c);
        var area = await Json<WorkAreaView>(await c.PostAsJsonAsync(d.Root + "/work-areas", new WorkAreaInput("Yeni alan", 2, null)), HttpStatusCode.Created);
        var edited = await Json<WorkAreaView>(await c.PutAsJsonAsync($"{d.Root}/work-areas/{area.Id}", new WorkAreaInput("Yeni ad", 3, area.Version)));
        await Problem(await c.PutAsJsonAsync($"{d.Root}/work-areas/{area.Id}", new WorkAreaInput("Eski", 1, area.Version)), 409, "scheduleConflict");
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync($"{d.Root}/work-areas/{area.Id}/archive", new VersionInput(edited.Version))).StatusCode);
        Assert.NotNull((await Json<WorkAreaView[]>(await c.GetAsync(d.Root + "/work-areas"))).Single(x => x.Id == area.Id).ArchivedAt);
        await Book(c, d);
        await Problem(await c.PostAsJsonAsync($"{d.Root}/work-areas/{d.Area}/archive", new VersionInput(d.AreaVersion)), 409, "areaInUse");
        await Problem(await c.PostAsJsonAsync(d.Root + "/work-areas", new WorkAreaInput("", 0, null)), 400, "validation");
    }
    [PostgresFact]
    public async Task RequirementListingUpsertDisableAndReenablePreserveHistoryAndGuardBookings()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c);
        var list = await Json<RequirementView[]>(await c.GetAsync($"{d.Root}/services/{d.Service}/scheduling-requirements?category=Car")); Assert.Single(list);
        var disabled = await Json<RequirementView>(await c.PostAsJsonAsync(d.RequirementPath + "/disable", new VersionInput(d.RequirementVersion))); Assert.False(disabled.SingleStageConfirmed);
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking()), 409, "unsupportedStages");
        await Problem(await c.PutAsJsonAsync(d.RequirementPath, new RequirementInput(d.Area, true, null)), 400, "validation");
        var enabled = await Json<RequirementView>(await c.PutAsJsonAsync(d.RequirementPath, new RequirementInput(d.Area, true, disabled.Version))); Assert.True(enabled.SingleStageConfirmed);
        var appointment = await Book(c, d);
        await Problem(await c.PostAsJsonAsync(d.RequirementPath + "/disable", new VersionInput(enabled.Version)), 409, "requirementInUse");
        await Json<AppointmentView>(await c.PostAsJsonAsync($"{d.Appointments}/{appointment.Id}/cancel", new VersionInput(appointment.Version)));
        // Disabling must still work when a catalog offering has been removed and the service disabled.
        await using (var db = Db()) { await db.ServiceOfferings.Where(x => x.ServiceId == d.Service).ExecuteDeleteAsync(); await db.BusinessServices.Where(x => x.Id == d.Service).ExecuteUpdateAsync(x => x.SetProperty(v => v.Enabled, false)); }
        Assert.False((await Json<RequirementView>(await c.PostAsJsonAsync(d.RequirementPath + "/disable", new VersionInput(enabled.Version)))).SingleStageConfirmed);
        await Problem(await c.PutAsJsonAsync(d.RequirementPath, new RequirementInput(d.Area, true, enabled.Version)), 409, "serviceUnavailable");
    }
    [PostgresFact]
    public async Task NewRequirementAndDatedEmployeeAvailabilityCrudAreSupported()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c);
        Assert.Equal(3, (await Json<EmployeeView[]>(await c.GetAsync(d.Root + "/scheduling-employees"))).Length);
        await using (var db = Db()) { db.Add(new ServiceOffering { ServiceId = d.Service, Category = "Suv", Price = 10, Currency = "TRY", ActiveMinutes = 20, Employees = 1 }); await db.SaveChangesAsync(); }
        var path = d.RequirementPath.Replace("/Car/", "/Suv/");
        var req = await Json<RequirementView>(await c.PutAsJsonAsync(path, new RequirementInput(d.Area, true, null))); Assert.Equal("Suv", req.Category);
        Assert.Single(await Json<RequirementView[]>(await c.GetAsync($"{d.Root}/services/{d.Service}/scheduling-requirements?category=Suv")));
        var window = await Json<AvailabilityView>(await c.PostAsJsonAsync(d.AvailabilityPath, new AvailabilityInput(Day.AddDays(5), Day.AddDays(5).AddHours(2), null)), HttpStatusCode.Created);
        var edited = await Json<AvailabilityView>(await c.PutAsJsonAsync(d.AvailabilityPath + "/" + window.Id, new AvailabilityInput(window.StartsAtUtc, window.EndsAtUtc.AddHours(1), window.Version)));
        await Problem(await c.PutAsJsonAsync(d.AvailabilityPath + "/" + window.Id, new AvailabilityInput(window.StartsAtUtc, window.EndsAtUtc, window.Version)), 409, "scheduleConflict");
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync($"{d.AvailabilityPath}/{window.Id}/archive", new VersionInput(edited.Version))).StatusCode);
        var windows = await Json<AvailabilityView[]>(await c.GetAsync(d.AvailabilityPath + "?fromUtc=2030-01-07T00:00:00Z&toUtcExclusive=2030-01-08T00:00:00Z&pageSize=1"));
        Assert.Single(windows); Assert.NotNull(windows[0].ArchivedAt);
        Assert.Empty(await Json<AvailabilityView[]>(await c.GetAsync(d.AvailabilityPath + "?fromUtc=2030-01-07T00:00:00Z&toUtcExclusive=2030-01-08T00:00:00Z&pageSize=1&page=2")));
        await Book(c, d);
        await Problem(await c.PostAsJsonAsync($"{d.AvailabilityPath}/{d.Windows[0]}/archive", new VersionInput(d.WindowVersion)), 409, "availabilityInUse");
    }
    [PostgresFact]
    public async Task CustomerAndEmployeeCannotReadOrMutateOwnerScheduling()
    {
        using var root = new ApiFactory(); using var api = Api(root);
        foreach (var role in new MembershipRole?[] { MembershipRole.Employee, null })
        {
            using var c = api.CreateClient(); var d = await Seed(api, c, role);
            await Problem(await c.GetAsync(d.Root + "/work-areas"), 403, "forbidden");
            await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking()), 403, "forbidden");
            await Problem(await c.PostAsJsonAsync(d.RequirementPath + "/disable", new VersionInput(d.RequirementVersion)), 403, "forbidden");
            await Problem(await c.GetAsync($"{d.Appointments}/{Guid.NewGuid()}/events"), 403, "forbidden");
        }
    }
    [PostgresFact]
    public async Task MembershipOwnerGrantAndSessionRevocationsAreCheckedOnEveryRequest()
    {
        using var root = new ApiFactory(); using var api = Api(root);
        for (var n = 0; n < 3; n++)
        {
            using var c = api.CreateClient(); var d = await Seed(api, c); await Json<WorkAreaView[]>(await c.GetAsync(d.Root + "/work-areas"));
            await using var db = Db();
            if (n == 0) await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == d.Membership).ExecuteDeleteAsync();
            if (n == 1) await db.BusinessMemberships.Where(x => x.Id == d.Membership).ExecuteUpdateAsync(x => x.SetProperty(m => m.RevokedAt, DateTimeOffset.UtcNow));
            if (n == 2) await db.DeviceSessions.Where(x => x.Id == d.Session).ExecuteUpdateAsync(x => x.SetProperty(m => m.RevokedAt, DateTimeOffset.UtcNow));
            await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking()), n == 2 ? 401 : 403, n == 2 ? "unauthenticated" : "forbidden");
        }
    }
    [PostgresFact]
    public async Task CrossBusinessResourcesReturnScopedErrorsWithoutLeakingIdentifiers()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var a = api.CreateClient(); using var b = api.CreateClient(); var x = await Seed(api, a); var y = await Seed(api, b); var appointment = await Book(a, x);
        await Problem(await b.GetAsync($"{x.Appointments}/{appointment.Id}"), 403, "forbidden", x.Business.ToString(), appointment.Id.ToString());
        await Problem(await b.GetAsync($"{y.Appointments}/{appointment.Id}"), 404, "resourceNotFound", x.Business.ToString(), appointment.Id.ToString());
        await Problem(await b.GetAsync($"{y.Appointments}/{appointment.Id}/events"), 404, "resourceNotFound");
        await Problem(await b.PostAsJsonAsync($"{y.Appointments}/{appointment.Id}/cancel", new VersionInput(appointment.Version)), 404, "resourceNotFound");
        await Problem(await a.PostAsJsonAsync(x.Appointments, x.Booking() with { LocalVehicleId = y.Vehicle }), 404, "resourceNotFound");
        await Problem(await a.PostAsJsonAsync(x.Appointments, x.Booking() with { LocalCustomerId = y.Customer }), 404, "resourceNotFound");
        await Problem(await a.PostAsJsonAsync(x.Appointments, x.Booking() with { ServiceId = y.Service }), 404, "resourceNotFound");
        await Problem(await a.PostAsJsonAsync(x.Appointments, x.Booking() with { EmployeeMembershipIds = [y.Employees[0]] }), 409, "employeeUnavailable");
        await Problem(await a.PutAsJsonAsync(x.RequirementPath, new RequirementInput(y.Area, true, x.RequirementVersion)), 404, "resourceNotFound");
        await Problem(await a.GetAsync($"{x.Root}/services/{y.Service}/scheduling-requirements"), 404, "resourceNotFound");
        await Problem(await a.GetAsync($"{x.Root}/employees/{y.Employees[0]}/availability?fromUtc=2030-01-02T00:00:00Z&toUtcExclusive=2030-01-03T00:00:00Z"), 404, "resourceNotFound");
    }
    [PostgresFact]
    public async Task InvalidShapesRequiredIdempotencyAndVersionsAndBoundsReturnSafe400()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c);
        foreach (var body in new[] { "null", "{}", "{", "[]", "{\"clientRequestId\":\"not-a-uuid\"}", "{\"clientRequestId\":\"" + new string('a', 500) + "\"}" })
            await Problem(await c.PostAsync(d.Appointments, new StringContent(body, Encoding.UTF8, "application/json")), 400, "validation");
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking() with { ClientRequestId = Guid.Empty }), 400, "validation");
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking() with { EmployeeMembershipIds = [d.Employees[0], d.Employees[0]] }), 400, "validation");
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking() with { Source = "Customer" }), 400, "validation");
        await Problem(await c.PostAsync(d.RequirementPath.Replace("scheduling-requirement", "scheduling-requirement/disable"), new StringContent("{}", Encoding.UTF8, "application/json")), 400, "validation");
        await Problem(await c.PutAsJsonAsync(d.RequirementPath, new { workAreaId = d.Area, version = d.RequirementVersion }), 400, "validation");
        foreach (var query in new[] { "", "?fromDate=oops&toDateExclusive=2030-01-03", "?fromDate=2030-01-02&toDateExclusive=2030-03-03", "?fromDate=2030-01-02&toDateExclusive=2030-01-03&pageSize=51", "?fromDate=2030-01-02&toDateExclusive=2030-01-03&status=999" })
            await Problem(await c.GetAsync(d.Appointments + query), 400, "validation");
        await Problem(await c.GetAsync(d.AvailabilityPath + "?fromUtc=2030-01-02T00:00:00Z&toUtcExclusive=2030-03-03T00:00:00Z"), 400, "validation");
        await using var db = Db(); Assert.Equal(0, await db.Appointments.CountAsync(x => x.BusinessId == d.Business));
    }
    [PostgresFact]
    public async Task IdempotentRetryReturnsOriginalResponseAfterRescheduleAndConfigurationChanges()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c); var input = d.Booking();
        using var first = await c.PostAsJsonAsync(d.Appointments, input); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync(); var original = JsonSerializer.Deserialize<AppointmentView>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal($"{d.Appointments}/{original.Id}", first.Headers.Location?.ToString());
        await Json<AppointmentView>(await c.PostAsJsonAsync($"{d.Appointments}/{original.Id}/reschedule", new RescheduleInput(original.Version, "2030-01-02T12:00", null, [d.Employees[1]])));
        await using (var db = Db()) { await db.Businesses.Where(x => x.Id == d.Business).ExecuteUpdateAsync(x => x.SetProperty(b => b.TimeZoneId, "Europe/Istanbul")); await db.BusinessServices.Where(x => x.Id == d.Service).ExecuteUpdateAsync(x => x.SetProperty(s => s.Enabled, false)); }
        using var retry = await c.PostAsJsonAsync(d.Appointments, input); Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(text, await retry.Content.ReadAsStringAsync());
        await Problem(await c.PostAsJsonAsync(d.Appointments, input with { StartLocal = "2030-01-02T11:00" }), 409, "idempotencyConflict");
        await using var verify = Db(); Assert.Equal(1, await verify.Appointments.CountAsync(x => x.BusinessId == d.Business)); Assert.Equal(1, await verify.SchedulingCreateReceipts.CountAsync(x => x.BusinessId == d.Business));
    }
    [PostgresFact]
    public async Task StaleVersionsAndCapacityEmployeeConflictsHaveDeterministic409Responses()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c, capacity: 2);
        var first = await Book(c, d);
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking()), 409, "resourceConflict"); // Employee conflict with spare area capacity.
        await Book(c, d, d.Booking(employee: 1));
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking(employee: 2)), 409, "resourceConflict");
        var moved = await Json<AppointmentView>(await c.PostAsJsonAsync($"{d.Appointments}/{first.Id}/reschedule", new RescheduleInput(first.Version, "2030-01-02T12:00", null, [d.Employees[0]])));
        await Problem(await c.PostAsJsonAsync($"{d.Appointments}/{first.Id}/cancel", new VersionInput(first.Version)), 409, "scheduleConflict");
        await Problem(await c.PostAsJsonAsync($"{d.Appointments}/{first.Id}/reschedule", new RescheduleInput(first.Version, "2030-01-02T13:00", null, [d.Employees[0]])), 409, "scheduleConflict");
        await Problem(await c.PostAsJsonAsync($"{d.Appointments}/{first.Id}/cancel", new { }), 400, "validation");
        await Json<AppointmentView>(await c.PostAsJsonAsync($"{d.Appointments}/{first.Id}/cancel", new VersionInput(moved.Version)));
    }
    [PostgresFact]
    public async Task CalendarUsesBusinessLocalDatesAndReturnsUtcInstantsWithBoundedPages()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c, zone: "Europe/Istanbul", allDay: true);
        var first = await Book(c, d, d.Booking("2030-01-02T00:30")); Assert.Equal(Day.AddHours(-2.5), first.StartsAtUtc); Assert.Equal("Europe/Istanbul", first.TimeZoneId);
        await Book(c, d, d.Booking("2030-01-02T01:00"));
        Assert.Single(await Json<AppointmentView[]>(await c.GetAsync(d.Appointments + "?fromDate=2030-01-02&toDateExclusive=2030-01-03&pageSize=1")));
        Assert.Single(await Json<AppointmentView[]>(await c.GetAsync(d.Appointments + "?fromDate=2030-01-02&toDateExclusive=2030-01-03&pageSize=1&page=2")));
        Assert.Empty(await Json<AppointmentView[]>(await c.GetAsync(d.Appointments + "?fromDate=2030-01-01&toDateExclusive=2030-01-02")));
    }
    private sealed class LockGate(int count) : DbCommandInterceptor
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentBag<int> Connections { get; } = [];
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Businesses\"", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Connections.Add(((NpgsqlConnection)command.Connection!).ProcessID);
                if (Interlocked.Increment(ref arrived) == count) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
    [PostgresFact]
    public async Task ConcurrentHttpBookingsForLastSlotHaveOneWinnerAcrossIndependentConnections()
    {
        var gate = new LockGate(2); using var root = new ApiFactory(); using var api = Api(root, gate); using var c = api.CreateClient(); var d = await Seed(api, c);
        var results = await Task.WhenAll(c.PostAsJsonAsync(d.Appointments, d.Booking()), c.PostAsJsonAsync(d.Appointments, d.Booking(employee: 1)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); await Problem(results.Single(r => r.StatusCode == HttpStatusCode.Conflict), 409, "resourceConflict");
        results.Single(r => r.StatusCode == HttpStatusCode.Created).Dispose(); Assert.Equal(2, gate.Connections.Distinct().Count());
        await using var db = Db(); Assert.Equal(1, await db.Appointments.CountAsync(x => x.BusinessId == d.Business)); Assert.Equal(2, await db.AppointmentResourceReservations.CountAsync(x => x.BusinessId == d.Business));
    }
    [PostgresFact]
    public async Task ConcurrentIdenticalHttpRequestsHaveOne201AndOne200AndIdenticalBodies()
    {
        var gate = new LockGate(2); using var root = new ApiFactory(); using var api = Api(root, gate); using var c = api.CreateClient(); var d = await Seed(api, c); var input = d.Booking();
        var results = await Task.WhenAll(c.PostAsJsonAsync(d.Appointments, input), c.PostAsJsonAsync(d.Appointments, input));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(await results[0].Content.ReadAsStringAsync(), await results[1].Content.ReadAsStringAsync()); foreach (var r in results) r.Dispose();
        Assert.Equal(2, gate.Connections.Distinct().Count()); await using var db = Db(); Assert.Equal(1, await db.SchedulingCreateReceipts.CountAsync(x => x.BusinessId == d.Business));
    }
    private sealed class FailRead : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"WorkAreas\"", StringComparison.Ordinal)) throw new InvalidOperationException("SENSITIVE SQL Password internal tenant details");
            return ValueTask.FromResult(result);
        }
    }
    [PostgresFact]
    public async Task InternalSchedulingFailureProducesSafe500ProblemDetails()
    {
        using var root = new ApiFactory(); using var api = Api(root, new FailRead()); using var c = api.CreateClient(); var d = await Seed(api, c);
        await Problem(await c.GetAsync(d.Root + "/work-areas"), 500, "unexpected", d.Business.ToString());
    }
    [PostgresFact]
    public async Task OpenApiDiscoversEverySchedulingOperationAndItsSecurityAndContracts()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); await Seed(api, c);
        using var response = await c.GetAsync("/openapi/v1.json"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var doc = json.RootElement;
        Assert.Equal("bearer", doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var operations = doc.GetProperty("paths").EnumerateObject().SelectMany(p => p.Value.EnumerateObject()).ToArray(); Assert.Equal(19, operations.Length);
        foreach (var op in operations)
        {
            Assert.True(op.Value.TryGetProperty("operationId", out _)); Assert.True(op.Value.TryGetProperty("security", out _));
            foreach (var code in new[] { "400", "401", "403", "404", "409", "500" }) Assert.True(op.Value.GetProperty("responses").TryGetProperty(code, out _));
        }
        var create = doc.GetProperty("paths").GetProperty("/api/v1/businesses/{businessId}/appointments").GetProperty("post");
        Assert.True(create.GetProperty("responses").TryGetProperty("201", out _)); Assert.True(create.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(create.GetProperty("requestBody").GetProperty("required").GetBoolean());
    }
    private sealed class RevokeAfterBoundaryCheck(DbContextOptions<WashFlowDbContext> options) : IBusinessAuthorizationStore
    {
        public async Task<bool> HasActiveGrantAsync(Guid userId, Guid businessId, MembershipRole role, CancellationToken ct)
        {
            await using var db = new WashFlowDbContext(options);
            var membership = await db.BusinessMemberships.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.UserId == userId && x.RevokedAt == null, ct);
            if (membership == null) return false;
            var allowed = await db.BusinessMembershipRoles.AnyAsync(x => x.BusinessMembershipId == membership.Id && x.Role == role, ct);
            if (allowed) await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == membership.Id && x.Role == role).ExecuteDeleteAsync(ct);
            return allowed;
        }
    }
    [PostgresFact]
    public async Task TransactionRechecksOwnerGrantRevokedAfterApiBoundaryAuthorization()
    {
        using var root = new ApiFactory(); using var api = Api(root, configure: s =>
        {
            s.RemoveAll<IBusinessAuthorizationStore>(); s.AddScoped<IBusinessAuthorizationStore>(_ => new RevokeAfterBoundaryCheck(fixture.Options));
        });
        using var c = api.CreateClient(); var d = await Seed(api, c);
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking()), 403, "forbidden");
        await using var db = Db(); Assert.Equal(0, await db.Appointments.CountAsync(x => x.BusinessId == d.Business)); Assert.Equal(0, await db.SchedulingCreateReceipts.CountAsync(x => x.BusinessId == d.Business));
    }
    [PostgresFact]
    public async Task InactiveResourcesAndMismatchedLocalCustomerCannotBeBooked()
    {
        using var root = new ApiFactory(); using var api = Api(root); using var c = api.CreateClient(); var d = await Seed(api, c);
        await using (var db = Db())
        {
            var other = new LocalCustomer { BusinessId = d.Business, FullName = "Diğer", NormalizedName = "DİĞER" }; db.Add(other); await db.SaveChangesAsync();
            await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking() with { LocalCustomerId = other.Id }), 404, "resourceNotFound");
            await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == d.Employees[0]).ExecuteDeleteAsync();
        }
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking()), 409, "employeeUnavailable");
        await using (var db = Db()) { await db.WorkAreas.Where(x => x.Id == d.Area).ExecuteUpdateAsync(x => x.SetProperty(a => a.ArchivedAt, DateTimeOffset.UtcNow)); }
        await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking(employee: 1)), 409, "areaUnavailable");
        await using (var db = Db()) { await db.WorkAreas.Where(x => x.Id == d.Area).ExecuteUpdateAsync(x => x.SetProperty(a => a.ArchivedAt, (DateTimeOffset?)null)); await db.LocalVehicles.Where(x => x.Id == d.Vehicle).ExecuteUpdateAsync(x => x.SetProperty(a => a.ArchivedAt, DateTimeOffset.UtcNow)); }
        // Reload the vehicle version so the lifecycle error, rather than a stale version, is tested.
        await using (var db = Db())
        {
            var vehicle = await db.LocalVehicles.SingleAsync(x => x.Id == d.Vehicle); var version = db.Entry(vehicle).Property<uint>("Version").CurrentValue;
            await Problem(await c.PostAsJsonAsync(d.Appointments, d.Booking(employee: 1) with { ExpectedVehicleVersion = version }), 409, "serviceUnavailable");
        }
    }

}
