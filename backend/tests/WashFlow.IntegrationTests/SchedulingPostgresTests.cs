using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Domain.Scheduling;
using WashFlow.Infrastructure.Persistence;
using WashFlow.Infrastructure.Scheduling;

namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class SchedulingPostgresTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private WashFlowDbContext Db() => new(fixture.Options);
    private EfSchedulingService Service(WashFlowDbContext db) => new(db, new Clock());
    private sealed record SeedData(Guid Business, SchedulingActor Actor, Guid Customer, Guid Vehicle, Guid Service, uint ServiceVersion, uint VehicleVersion, WorkAreaView Area, Guid[] Employees, AvailabilityView[] Windows)
    {
        public BookingInput Booking(string time = "2030-01-02T10:00", int employee = 0) => new(Guid.NewGuid(), Customer, Vehicle, Service, ServiceVersion, VehicleVersion, time, null, [Employees[employee]]);
    }
    private async Task<SeedData> Seed(int capacity = 1, int duration = 30, string zone = "Etc/UTC")
    {
        await using var db = Db();
        Assert.Equal(17, (await db.Database.SqlQueryRaw<int>("SELECT current_setting('server_version_num')::int AS \"Value\"").SingleAsync()) / 10000);
        var u = new User("Sahip", $"{Guid.NewGuid():N}@example.test", "hash");
        var b = new Business { Name = "Planlama", TimeZoneId = zone };
        var owner = new BusinessMembership { BusinessId = b.Id, UserId = u.Id };
        var session = new DeviceSession { UserId = u.Id, SecurityStamp = u.SecurityStamp, DeviceName = "Test", CreatedAt = Now.AddMinutes(-1), ExpiresAt = Now.AddDays(1) };
        var c = new LocalCustomer { BusinessId = b.Id, FullName = "Müşteri", NormalizedName = "MÜŞTERİ" };
        var v = new LocalVehicle { BusinessId = b.Id, LocalCustomerId = c.Id, Plate = "34 TEST 1", NormalizedPlate = "34TEST1", Brand = "Test", Model = "Test", Category = "Car" };
        var s = new BusinessService { BusinessId = b.Id, Name = "Yıkama", SearchName = "YIKAMA", Enabled = true };
        db.AddRange(u, b, owner, session, c, v, s, new BusinessMembershipRole { BusinessMembershipId = owner.Id, Role = MembershipRole.Owner },
            new ServiceOffering { ServiceId = s.Id, Category = "Car", Price = 450.10m, Currency = "TRY", ActiveMinutes = duration, WaitingMinutes = 60, Employees = 1 });
        for (var day = 1; day <= 7; day++) db.Add(new WeeklyOpeningDay { BusinessId = b.Id, Day = day, OpensAtMinute = 8 * 60, ClosesAtMinute = 20 * 60 });
        var employees = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var e = new User("Çalışan", $"{Guid.NewGuid():N}@example.test", "hash"); var m = new BusinessMembership { BusinessId = b.Id, UserId = e.Id };
            db.AddRange(e, m, new BusinessMembershipRole { BusinessMembershipId = m.Id, Role = MembershipRole.Employee }); employees.Add(m.Id);
        }
        await db.SaveChangesAsync();
        var sv = db.Entry(s).Property<uint>("Version").CurrentValue; var vv = db.Entry(v).Property<uint>("Version").CurrentValue;
        var actor = new SchedulingActor(u.Id, session.Id); var service = Service(db);
        var area = await service.SaveWorkArea(actor, b.Id, null, new("Yıkama alanı", capacity, null));
        await service.SaveRequirement(actor, b.Id, s.Id, "Car", new(area.Id, true, null));
        var windows = new List<AvailabilityView>();
        foreach (var e in employees) windows.Add(await service.SaveAvailability(actor, b.Id, e, null, new(Now.AddDays(1).AddHours(8), Now.AddDays(1).AddHours(20), null)));
        return new(b.Id, actor, c.Id, v.Id, s.Id, sv, vv, area, employees.ToArray(), windows.ToArray());
    }
    private async Task<SchedulingResult<AppointmentView>> Create(SeedData s, BookingInput input)
    {
        await using var db = Db(); return await Service(db).Create(s.Actor, s.Business, input);
    }
    private static async Task Fault(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<SchedulingFault>(action)).Code);

    [PostgresFact]
    public async Task CreatePersistsSnapshotsReservationsEventsAndOriginalRetryAfterLifecycleAndCatalogChanges()
    {
        var s = await Seed(); var input = s.Booking(); var created = await Create(s, input); Assert.True(created.Created);
        Assert.Equal("450.10", created.Value.Price); Assert.Equal(30, created.Value.ActiveMinutes); Assert.Equal(60, created.Value.WaitingMinutes);
        Assert.Equal(created.Value.StartsAtUtc.AddMinutes(30), created.Value.ActiveEndsAtUtc); Assert.Equal(2, created.Value.Reservations.Count); Assert.Single(created.Value.Events);
        await using (var db = Db())
        {
            var detail = await Service(db).Detail(s.Actor, s.Business, created.Value.Id); Assert.Equal(created.Value.Version, detail.Version);
            var moved = await Service(db).Reschedule(s.Actor, s.Business, detail.Id, new(detail.Version, "2030-01-02T11:00", null, [s.Employees[1]]));
            Assert.NotEqual(detail.Version, moved.Version); Assert.Equal(4, moved.Reservations.Count); Assert.Equal(2, moved.Reservations.Count(x => x.ReleasedAt != null)); Assert.Equal(2, moved.Events.Count);
            var cancelled = await Service(db).Cancel(s.Actor, s.Business, moved.Id, new(moved.Version));
            Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status); Assert.Equal(3, cancelled.Events.Count); Assert.All(cancelled.Reservations, x => Assert.NotNull(x.ReleasedAt));
            var catalog = await db.BusinessServices.SingleAsync(x => x.Id == s.Service); catalog.Enabled = false;
            var offering = await db.ServiceOfferings.SingleAsync(x => x.ServiceId == s.Service); offering.Price = 900; offering.ActiveMinutes = 90;
            var business = await db.Businesses.SingleAsync(x => x.Id == s.Business); business.TimeZoneId = "Europe/Istanbul"; await db.SaveChangesAsync();
        }
        var retry = await Create(s, input); Assert.False(retry.Created); Assert.Equal(System.Text.Json.JsonSerializer.Serialize(created.Value), System.Text.Json.JsonSerializer.Serialize(retry.Value));
        await using var verify = Db(); Assert.Single(await verify.Appointments.Where(x => x.BusinessId == s.Business).ToListAsync());
        Assert.Single(await verify.SchedulingCreateReceipts.Where(x => x.BusinessId == s.Business).ToListAsync());
        Assert.Equal(3, await verify.AppointmentEvents.CountAsync(x => x.BusinessId == s.Business));
    }
    [PostgresFact]
    public async Task SameKeyDifferentRequestConflictsAndConcurrentIdenticalRequestsPersistOnce()
    {
        var s = await Seed(); var input = s.Booking();
        var results = await Concurrent(s, input, input, input);
        Assert.All(results, x => Assert.Null(x.Code));
        Assert.Single(results, x => x.Created); Assert.Single(results.Select(x => x.Value!.Id).Distinct());
        await Fault("idempotencyConflict", () => Create(s, input with { StartLocal = "2030-01-02T11:00" }));
        await using var db = Db(); Assert.Equal(1, await db.Appointments.CountAsync(x => x.BusinessId == s.Business));
        Assert.Equal(1, await db.SchedulingCreateReceipts.CountAsync(x => x.BusinessId == s.Business));
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
    private async Task<(AppointmentView? Value, string? Code, bool Created)[]> Concurrent(SeedData s, params BookingInput[] inputs)
    {
        var gate = new LockGate(inputs.Length);
        var options = new DbContextOptionsBuilder<WashFlowDbContext>(fixture.Options).AddInterceptors(gate).Options;
        async Task<(AppointmentView?, string?, bool)> Attempt(BookingInput input)
        {
            await using var db = new WashFlowDbContext(options);
            try { var r = await Service(db).Create(s.Actor, s.Business, input); return (r.Value, null, r.Created); }
            catch (SchedulingFault e) { return (null, e.Code, false); }
        }
        var results = await Task.WhenAll(inputs.Select(Attempt));
        Assert.Equal(inputs.Length, gate.Connections.Distinct().Count());
        return results;
    }
    [PostgresFact]
    public async Task ConcurrentLastSlotHasOneWinnerWithIndependentConnections()
    {
        var s = await Seed();
        var result = await Concurrent(s, s.Booking(employee: 0), s.Booking(employee: 1));
        Assert.Single(result, x => x.Value != null); Assert.Single(result, x => x.Code == "resourceConflict");
        await using var db = Db(); Assert.Equal(1, await db.Appointments.CountAsync(x => x.BusinessId == s.Business)); Assert.Equal(2, await db.AppointmentResourceReservations.CountAsync(x => x.BusinessId == s.Business));
    }
    [PostgresFact]
    public async Task CapacityTwoAllowsTwoButRejectsThirdAndSameEmployeeCollision()
    {
        var s = await Seed(2);
        var result = await Concurrent(s, s.Booking(employee: 0), s.Booking(employee: 1), s.Booking(employee: 2));
        Assert.Equal(2, result.Count(x => x.Value != null)); Assert.Single(result, x => x.Code == "resourceConflict");
        var winner = result.First(x => x.Value != null).Value!;
        var employee = winner.Reservations.Single(x => x.EmployeeMembershipId != null).EmployeeMembershipId!.Value;
        await Fault("resourceConflict", () => Create(s, s.Booking() with { EmployeeMembershipIds = [employee] }));
    }
    [PostgresFact]
    public async Task ActualPeakAndHalfOpenAdjacencyAreUsedInsteadOfOverlapCount()
    {
        var s = await Seed(2, 30);
        await Create(s, s.Booking("2030-01-02T10:00", 0)); await Create(s, s.Booking("2030-01-02T10:30", 0));
        // Candidate overlaps both existing jobs, but their peak with the candidate is only 2.
        await Create(s, s.Booking("2030-01-02T10:15", 1));
        await Create(s, s.Booking("2030-01-02T11:00", 0));
        await using var db = Db(); Assert.Equal(4, await db.Appointments.CountAsync(x => x.BusinessId == s.Business));
    }
    [PostgresFact]
    public async Task ReschedulingAndCancellationCheckVersionsAndReleaseResourcesAtomically()
    {
        var s = await Seed(); var first = (await Create(s, s.Booking())).Value;
        var second = (await Create(s, s.Booking("2030-01-02T11:00", 1))).Value;
        await using var db = Db(); var engine = Service(db);
        await Fault("scheduleConflict", () => engine.Reschedule(s.Actor, s.Business, first.Id, new(first.Version + 1, "2030-01-02T12:00", null, [s.Employees[0]])));
        await Fault("resourceConflict", () => engine.Reschedule(s.Actor, s.Business, first.Id, new(first.Version, "2030-01-02T11:00", null, [s.Employees[0]])));
        var unchanged = await engine.Detail(s.Actor, s.Business, first.Id); Assert.Equal(first.Version, unchanged.Version); Assert.Single(unchanged.Events); Assert.All(unchanged.Reservations, x => Assert.Null(x.ReleasedAt));
        var moved = await engine.Reschedule(s.Actor, s.Business, first.Id, new(first.Version, "2030-01-02T12:00", null, [s.Employees[0]]));
        await Fault("scheduleConflict", () => engine.Cancel(s.Actor, s.Business, first.Id, new(first.Version)));
        await Create(s, s.Booking());
        var cancelled = await engine.Cancel(s.Actor, s.Business, second.Id, new(second.Version));
        Assert.All(cancelled.Reservations, r => Assert.NotNull(r.ReleasedAt));
        await Fault("invalidStatus", () => engine.Reschedule(s.Actor, s.Business, second.Id, new(cancelled.Version, "2030-01-02T13:00", null, [s.Employees[1]])));
        await Create(s, s.Booking("2030-01-02T11:00", 1)); Assert.NotEqual(first.Version, moved.Version);
    }
    [PostgresFact]
    public async Task TenantAndCustomerVehicleBoundariesAndOwnerSessionGrantsAreRechecked()
    {
        var a = await Seed(); var b = await Seed(); var created = (await Create(a, a.Booking())).Value;
        await using var db = Db(); var e = Service(db);
        await Fault("forbidden", () => e.Detail(b.Actor, a.Business, created.Id));
        await Fault("resourceNotFound", () => e.Detail(b.Actor, b.Business, created.Id));
        await Fault("resourceNotFound", () => e.Cancel(b.Actor, b.Business, created.Id, new(created.Version)));
        await Fault("resourceNotFound", () => Create(a, a.Booking() with { LocalVehicleId = b.Vehicle }));
        await Fault("resourceNotFound", () => Create(a, a.Booking() with { LocalCustomerId = b.Customer }));
        await Fault("resourceNotFound", () => Create(a, a.Booking() with { ServiceId = b.Service }));
        await Fault("employeeUnavailable", () => Create(a, a.Booking() with { EmployeeMembershipIds = [b.Employees[0]] }));
        var requirement = (await e.Requirements(a.Actor, a.Business)).Single();
        await Fault("resourceNotFound", () => e.SaveRequirement(a.Actor, a.Business, a.Service, "Car", new(b.Area.Id, true, requirement.Version)));
        await Fault("resourceNotFound", () => e.SaveAvailability(a.Actor, a.Business, a.Employees[0], b.Windows[0].Id, new(Now.AddDays(1), Now.AddDays(2), b.Windows[0].Version)));
        await Fault("unauthorized", () => e.WorkAreas(a.Actor with { SessionId = b.Actor.SessionId }, a.Business));
        var member = await db.BusinessMemberships.SingleAsync(x => x.BusinessId == a.Business && x.UserId == a.Actor.UserId);
        await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == member.Id).ExecuteDeleteAsync(); db.ChangeTracker.Clear();
        await Fault("forbidden", () => e.WorkAreas(a.Actor, a.Business));
    }
    [PostgresFact]
    public async Task ConfigurationGuardsKeepExistingReservationsCovered()
    {
        var s = await Seed(2); await Create(s, s.Booking()); await Create(s, s.Booking(employee: 1));
        await using var db = Db(); var e = Service(db);
        await Fault("capacityInUse", () => e.SaveWorkArea(s.Actor, s.Business, s.Area.Id, new("Alan", 1, s.Area.Version)));
        await Fault("areaInUse", () => e.ArchiveWorkArea(s.Actor, s.Business, s.Area.Id, new(s.Area.Version)));
        await Fault("availabilityInUse", () => e.ArchiveAvailability(s.Actor, s.Business, s.Employees[0], s.Windows[0].Id, new(s.Windows[0].Version)));
        await Fault("availabilityInUse", () => e.SaveAvailability(s.Actor, s.Business, s.Employees[0], s.Windows[0].Id, new(Now.AddDays(1).AddHours(11), Now.AddDays(1).AddHours(12), s.Windows[0].Version)));
        var requirement = (await e.Requirements(s.Actor, s.Business)).Single();
        await Fault("requirementInUse", () => e.SaveRequirement(s.Actor, s.Business, s.Service, "Car", new(s.Area.Id, false, requirement.Version)));
        await Fault("validation", () => e.SaveWorkArea(s.Actor, s.Business, null, new("", 0, null)));
        var area = await e.SaveWorkArea(s.Actor, s.Business, null, new("Boş alan", 1, null));
        await e.ArchiveWorkArea(s.Actor, s.Business, area.Id, new(area.Version));
        var window = await e.SaveAvailability(s.Actor, s.Business, s.Employees[0], null, new(Now.AddDays(2), Now.AddDays(3), null));
        await e.ArchiveAvailability(s.Actor, s.Business, s.Employees[0], window.Id, new(window.Version));
        Assert.NotEmpty(await e.Employees(s.Actor, s.Business)); Assert.Equal(2, (await e.WorkAreas(s.Actor, s.Business)).Count);
    }
    [PostgresFact]
    public async Task PreviewCalendarHoursAndAvailabilityAreAuthoritativeButPreviewDoesNotReserve()
    {
        var s = await Seed(); await using var db = Db(); var e = Service(db);
        var input = new AvailabilityCheckInput(s.Service, "Car", "2030-01-02T10:00", null, [s.Employees[0]]);
        Assert.True((await e.Preview(s.Actor, s.Business, input)).Available); Assert.True((await e.Preview(s.Actor, s.Business, input)).Available);
        await Create(s, s.Booking()); Assert.False((await e.Preview(s.Actor, s.Business, input)).Available);
        Assert.True((await e.Preview(s.Actor, s.Business, input with { StartLocal = "2030-01-02T10:30" })).Available);
        await Fault("outsideHours", () => Create(s, s.Booking("2030-01-02T19:45")));
        await Fault("resourceConflict", () => Create(s, s.Booking("2030-01-03T10:00")));
        var calendar = await e.Calendar(s.Actor, s.Business, new(new(2030, 1, 2), new(2030, 1, 3))); Assert.Single(calendar);
        Assert.Empty(await e.Calendar(s.Actor, s.Business, new(new(2030, 1, 2), new(2030, 1, 3), Status: AppointmentStatus.Cancelled)));
        await Fault("validation", () => e.Calendar(s.Actor, s.Business, new(new(2030, 1, 2), new(2030, 3, 3))));
    }
    [PostgresFact]
    public async Task StaleCatalogAndVehicleVersionsAndInactiveGrantsCannotCreate()
    {
        var s = await Seed();
        await Fault("scheduleConflict", () => Create(s, s.Booking() with { ExpectedServiceVersion = s.ServiceVersion + 1 }));
        await Fault("scheduleConflict", () => Create(s, s.Booking() with { ExpectedVehicleVersion = s.VehicleVersion + 1 }));
        await using var db = Db(); await db.BusinessMembershipRoles.Where(x => x.BusinessMembershipId == s.Employees[0]).ExecuteDeleteAsync();
        await Fault("employeeUnavailable", () => Create(s, s.Booking()));
        Assert.Empty(await db.Appointments.Where(x => x.BusinessId == s.Business).ToListAsync()); Assert.Empty(await db.SchedulingCreateReceipts.Where(x => x.BusinessId == s.Business).ToListAsync());
    }
    [PostgresFact]
    public async Task CompositeForeignKeysRejectCrossTenantServiceVehicleAreaEmployeeAndAppointmentReferences()
    {
        var a = await Seed(); var b = await Seed();
        await using var db = Db();
        var bad = ServiceSchedulingRequirement.Create(a.Business, b.Service, "Car", a.Area.Id, true, Now);
        db.Add(bad); await ForeignKey(db);
        db.Add(ServiceSchedulingRequirement.Create(a.Business, a.Service, "Other", b.Area.Id, true, Now)); await ForeignKey(db);
        db.Add(EmployeeAvailability.Create(a.Business, b.Employees[0], new(Now, Now.AddHours(1)), Now)); await ForeignKey(db);
        var appt = (await Create(a, a.Booking())).Value;
        db.Add(AppointmentResourceReservation.Create(b.Business, appt.Id, b.Area.Id, null, new(Now, Now.AddHours(1)), Now)); await ForeignKey(db);
        db.Add(AppointmentResourceReservation.Create(a.Business, appt.Id, null, b.Employees[0], new(Now, Now.AddHours(1)), Now)); await ForeignKey(db);
        var snapshot = new AppointmentSnapshot("Test", "Test", "34 TEST", "Car", 1, "TRY", 30, null, 1, "Etc/UTC");
        db.Add(Appointment.Create(a.Business, a.Customer, b.Vehicle, a.Service, a.Area.Id, a.Actor.UserId, Guid.NewGuid(), new string('a',64), BookingSource.OwnerPhone, snapshot, Now, [a.Employees[0]], Now)); await ForeignKey(db);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Appointments\" WHERE \"Id\" = {appt.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }
    private static async Task ForeignKey(WashFlowDbContext db)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState); db.ChangeTracker.Clear();
    }
    private static async Task InjectedFailure(Func<Task> action, string expected)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(action);
        Assert.Equal(expected, Assert.IsType<InvalidOperationException>(error.InnerException).Message);
    }
    private sealed class FailReceipt : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"SchedulingCreateReceipts\"", StringComparison.Ordinal)) throw new InvalidOperationException("Injected receipt failure");
            return ValueTask.FromResult(result);
        }
    }
    [PostgresFact]
    public async Task FailureAfterAggregateSaveRollsBackAppointmentEventsReservationsAndReceiptAndAllowsRetry()
    {
        var s = await Seed(); var input = s.Booking();
        await using (var db = new WashFlowDbContext(new DbContextOptionsBuilder<WashFlowDbContext>(fixture.Options).AddInterceptors(new FailReceipt()).Options))
            await InjectedFailure(() => Service(db).Create(s.Actor, s.Business, input), "Injected receipt failure");
        await using (var db = Db())
        {
            Assert.Empty(await db.Appointments.Where(x => x.BusinessId == s.Business).ToListAsync()); Assert.Empty(await db.AppointmentResourceReservations.Where(x => x.BusinessId == s.Business).ToListAsync());
            Assert.Empty(await db.AppointmentEvents.Where(x => x.BusinessId == s.Business).ToListAsync()); Assert.Empty(await db.SchedulingCreateReceipts.Where(x => x.BusinessId == s.Business).ToListAsync());
        }
        Assert.True((await Create(s, input)).Created);
    }
    private sealed class FailEvent : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"AppointmentEvents\"", StringComparison.Ordinal)) throw new InvalidOperationException("Injected event failure");
            return ValueTask.FromResult(result);
        }
    }
    [PostgresFact]
    public async Task RescheduleAndCancelFailuresRollBackAlreadyFlushedReleasesAndKeepOriginalVersion()
    {
        var s = await Seed(); var first = (await Create(s, s.Booking())).Value;
        var options = new DbContextOptionsBuilder<WashFlowDbContext>(fixture.Options).AddInterceptors(new FailEvent()).Options;
        await using (var db = new WashFlowDbContext(options))
            await InjectedFailure(() => Service(db).Reschedule(s.Actor, s.Business, first.Id, new(first.Version, "2030-01-02T12:00", null, [s.Employees[1]])), "Injected event failure");
        await using (var db = new WashFlowDbContext(options))
            await InjectedFailure(() => Service(db).Cancel(s.Actor, s.Business, first.Id, new(first.Version)), "Injected event failure");
        await using (var db = Db())
        {
            var actual = await Service(db).Detail(s.Actor, s.Business, first.Id);
            Assert.Equal(first.Version, actual.Version); Assert.Equal(first.StartsAtUtc, actual.StartsAtUtc); Assert.Equal(AppointmentStatus.Confirmed, actual.Status);
            Assert.Equal(2, actual.Reservations.Count); Assert.All(actual.Reservations, x => Assert.Null(x.ReleasedAt)); Assert.Single(actual.Events);
        }
        await Fault("resourceConflict", () => Create(s, s.Booking(employee: 1)));
        Assert.True((await Create(s, s.Booking("2030-01-02T12:00", 1))).Created);
    }
    [PostgresFact]
    public async Task EmployeeConflictIsRejectedWithSpareAreaCapacityIncludingConcurrentRequests()
    {
        var s = await Seed(2);
        var result = await Concurrent(s, s.Booking(), s.Booking());
        Assert.Single(result, x => x.Value != null); Assert.Single(result, x => x.Code == "resourceConflict");
        Assert.True((await Create(s, s.Booking(employee: 1))).Created);
    }
    [PostgresFact]
    public async Task DatabaseGuardsRejectCapacityIntervalsSnapshotAndHistoryMutation()
    {
        var s = await Seed(); var row = (await Create(s, s.Booking())).Value; await using var db = Db();
        async Task Check(Func<Task> command)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(command); Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        await Check(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WorkAreas\" SET \"Capacity\" = 0 WHERE \"Id\" = {s.Area.Id}"));
        await Check(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"EmployeeAvailabilities\" SET \"EndsAtUtc\" = \"StartsAtUtc\" WHERE \"Id\" = {s.Windows[0].Id}"));
        await Check(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Appointments\" SET \"ServiceName\" = 'Changed' WHERE \"Id\" = {row.Id}"));
        await Check(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentEvents\" SET \"Sequence\" = 99 WHERE \"AppointmentId\" = {row.Id}"));
        await Check(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AppointmentEvents\" WHERE \"AppointmentId\" = {row.Id}"));
        await Check(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"SchedulingCreateReceipts\" WHERE \"AppointmentId\" = {row.Id}"));
        await Service(db).Cancel(s.Actor, s.Business, row.Id, new(row.Version));
        await Check(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentResourceReservations\" SET \"ReleasedAt\" = NULL WHERE \"AppointmentId\" = {row.Id}"));
    }
    [PostgresFact]
    public async Task AvailabilityCoverageMergesAdjacentWindowsAndRejectsGapsAndStaleEdits()
    {
        var s = await Seed(); await using var db = Db(); var engine = Service(db);
        var first = await engine.SaveAvailability(s.Actor, s.Business, s.Employees[0], s.Windows[0].Id,
            new(Now.AddDays(1).AddHours(10), Now.AddDays(1).AddHours(10).AddMinutes(15), s.Windows[0].Version));
        await Fault("scheduleConflict", () => engine.SaveAvailability(s.Actor, s.Business, s.Employees[0], first.Id,
            new(Now.AddDays(1).AddHours(10), Now.AddDays(1).AddHours(11), s.Windows[0].Version)));
        await Fault("resourceConflict", () => Create(s, s.Booking()));
        await engine.SaveAvailability(s.Actor, s.Business, s.Employees[0], null,
            new(Now.AddDays(1).AddHours(10).AddMinutes(15), Now.AddDays(1).AddHours(11), null));
        Assert.True((await Create(s, s.Booking())).Created);
    }

    [PostgresFact]
    public async Task DstGapAndFoldAreValidatedUsingBusinessZoneAndRetryDistinguishesOccurrences()
    {
        var s = await Seed(zone: "Europe/Berlin");
        await Fault("invalidLocalTime", () => Create(s, s.Booking("2030-03-31T02:30")));
        await Fault("ambiguousLocalTime", () => Create(s, s.Booking("2030-10-27T02:30")));
        await using (var db = Db())
        {
            await db.WeeklyOpeningDays.Where(x => x.BusinessId == s.Business).ExecuteUpdateAsync(x => x.SetProperty(d => d.OpensAtMinute, 0).SetProperty(d => d.ClosesAtMinute, 1440));
            await Service(db).SaveAvailability(s.Actor, s.Business, s.Employees[0], null,
                new(new DateTimeOffset(2030,10,26,0,0,0,TimeSpan.Zero), new DateTimeOffset(2030,10,28,0,0,0,TimeSpan.Zero), null));
        }
        var input = s.Booking("2030-10-27T02:30") with { UtcOffsetMinutes = 120 };
        var first = await Create(s, input); Assert.Equal(0, first.Value.StartsAtUtc.Hour);
        Assert.False((await Create(s, input)).Created);
        await Fault("idempotencyConflict", () => Create(s, input with { UtcOffsetMinutes = 60 }));
        var second = await Create(s, input with { ClientRequestId = Guid.NewGuid(), UtcOffsetMinutes = 60 }); Assert.Equal(1, second.Value.StartsAtUtc.Hour);
    }

}
