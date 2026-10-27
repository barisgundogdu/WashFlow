using WashFlow.Application.Scheduling;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Scheduling;

namespace WashFlow.UnitTests;

public sealed class SchedulingDomainTests
{
    private static readonly Guid Business = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Employee = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Area = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);
    private static AppointmentSnapshot Snapshot(decimal price = 450.10m, int active = 20, int? waiting = null,
        int employees = 1, string category = "Car", string currency = "TRY", string name = "Dış yıkama") =>
        new(name, "Ayşe", "34 AB 123", category, price, currency, active, waiting, employees, "Europe/Istanbul");
    private static Appointment Book(AppointmentSnapshot? snapshot = null, string? hash = null, BookingSource source = BookingSource.OwnerPhone) =>
        Appointment.Create(Business, Actor, Employee, Area, Area, Actor, Employee, hash ?? new string('a', 64), source,
            snapshot ?? Snapshot(), Now.AddHours(2), [Employee], Now);

    [Fact]
    public void AuthoritativeSnapshotCopiesExistingCatalogAndLocalOwnershipValues()
    {
        var service = new BusinessService { BusinessId = Business, Name = "Hizmet", SearchName = "HIZMET", Enabled = true };
        var customer = new LocalCustomer { BusinessId = Business, FullName = "Ayşe", NormalizedName = "AYŞE" };
        var vehicle = new LocalVehicle { BusinessId = Business, LocalCustomerId = customer.Id, Plate = "34 AB 123", NormalizedPlate = "34AB123",
            Brand = "Test", Model = "Test", Category = "Car" };
        var offering = new ServiceOffering { ServiceId = service.Id, Category = "Car", Price = 450.10m, Currency = "TRY", ActiveMinutes = 20, WaitingMinutes = 0, Employees = 1 };
        var snapshot = SchedulingValidation.CreateSnapshot(service, offering, customer, vehicle, "Europe/Istanbul");
        Assert.Equal(("Hizmet", "Ayşe", "34 AB 123", "Car", 450.10m, 20, 0),
            (snapshot.ServiceName, snapshot.CustomerName, snapshot.VehiclePlate, snapshot.Category, snapshot.Price, snapshot.ActiveMinutes, snapshot.WaitingMinutes));
        offering.Price = 900; customer.FullName = "Changed"; vehicle.Category = "Suv";
        Assert.Equal(450.10m, snapshot.Price); Assert.Equal("Ayşe", snapshot.CustomerName); Assert.Equal("Car", snapshot.Category);
        Assert.Equal("invalidCategory", Assert.Throws<SchedulingFault>(() => SchedulingValidation.CreateSnapshot(service, offering, customer, vehicle, "Europe/Istanbul")).Code);
        vehicle.Category = "Car"; vehicle.ArchivedAt = Now;
        Assert.Throws<SchedulingFault>(() => SchedulingValidation.CreateSnapshot(service, offering, customer, vehicle, "Europe/Istanbul"));
        vehicle.ArchivedAt = null; service.Enabled = false;
        Assert.Throws<SchedulingFault>(() => SchedulingValidation.CreateSnapshot(service, offering, customer, vehicle, "Europe/Istanbul"));
        service.Enabled = true; service.ArchivedAt = Now;
        Assert.Throws<SchedulingFault>(() => SchedulingValidation.CreateSnapshot(service, offering, customer, vehicle, "Europe/Istanbul"));
    }
    [Theory] [InlineData("customer")] [InlineData("vehicle")] [InlineData("parent")] [InlineData("offering")]
    public void SnapshotRejectsForeignOrMismatchedRecordIdentities(string changed)
    {
        var service = new BusinessService { BusinessId = Business, Name = "Hizmet", SearchName = "HIZMET", Enabled = true };
        var customer = new LocalCustomer { BusinessId = changed == "customer" ? Actor : Business, FullName = "Ayşe", NormalizedName = "AYŞE" };
        var vehicle = new LocalVehicle { BusinessId = changed == "vehicle" ? Actor : Business,
            LocalCustomerId = changed == "parent" ? Actor : customer.Id, Plate = "34 AB 123", NormalizedPlate = "34AB123", Brand = "Test", Model = "Test", Category = "Car" };
        var offering = new ServiceOffering { ServiceId = changed == "offering" ? Actor : service.Id, Category = "Car", Price = 450.10m, Currency = "TRY", ActiveMinutes = 20, Employees = 1 };
        Assert.Equal(404, Assert.Throws<SchedulingFault>(() => SchedulingValidation.CreateSnapshot(service, offering, customer, vehicle, "Europe/Istanbul")).Status);
    }
    [Fact]
    public void SchedulingRequirementMustMatchTheExactTenantServiceCategoryAndArea()
    {
        var area = WorkArea.Create(Business, "İç alan", 2, Now);
        var requirement = ServiceSchedulingRequirement.Create(Business, Area, "Car", area.Id, true, Now);
        SchedulingRules.RequireWorkArea(Business, Area, "Car", requirement, area);
        Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireWorkArea(Actor, Area, "Car", requirement, area));
        Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireWorkArea(Business, Actor, "Car", requirement, area));
        Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireWorkArea(Business, Area, "Suv", requirement, area));
        var foreign = WorkArea.Create(Actor, "Foreign", 2, Now);
        Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireWorkArea(Business, Area, "Car", requirement, foreign));
        requirement.Update(area.Id, false, Now);
        Assert.Equal("unsupportedStages", Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireWorkArea(Business, Area, "Car", requirement, area)).Code);
        requirement.Update(area.Id, true, Now); area.Archive(Now);
        Assert.Equal("areaUnavailable", Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireWorkArea(Business, Area, "Car", requirement, area)).Code);
    }

    [Fact]
    public void CreationPreservesOwnershipAndReservesOnlyActiveResources()
    {
        var appointment = Book();
        Assert.NotEqual(Guid.Empty, appointment.Id);
        Assert.Equal(Business, appointment.BusinessId);
        Assert.Equal(Actor, appointment.CreatedByUserId);
        Assert.Equal(Employee, appointment.ClientRequestId);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(BookingSource.OwnerPhone, appointment.Source);
        Assert.Equal(Now.AddHours(2).AddMinutes(20), appointment.ActiveEndsAtUtc);
        Assert.Equal(2, appointment.Reservations.Count);
        Assert.Single(appointment.Reservations, x => x.WorkAreaId == Area && x.EmployeeMembershipId == null);
        Assert.Single(appointment.Reservations, x => x.WorkAreaId == null && x.EmployeeMembershipId == Employee);
        Assert.All(appointment.Reservations, x => {
            Assert.Equal(Business, x.BusinessId); Assert.Equal(appointment.Id, x.AppointmentId);
            Assert.Equal(appointment.ActiveEndsAtUtc, x.EndsAtUtc); Assert.Null(x.ReleasedAt);
        });
        var created = Assert.Single(appointment.Events);
        Assert.Equal((1, AppointmentEventKind.Created, AppointmentStatus.Confirmed), (created.Sequence, created.Kind, created.Status));
        Assert.Null(created.PreviousStart); Assert.Null(created.PreviousEnd);
    }
    [Theory] [InlineData(null)] [InlineData(0)] [InlineData(120)]
    public void WaitingIsSnapshottedWithoutReservingExtraTime(int? waiting)
    {
        var appointment = Book(Snapshot(waiting: waiting));
        Assert.Equal(waiting, appointment.WaitingMinutes);
        Assert.Equal(20, (appointment.ActiveEndsAtUtc - appointment.StartsAtUtc).TotalMinutes);
        Assert.All(appointment.Reservations, x => Assert.Equal(appointment.ActiveEndsAtUtc, x.EndsAtUtc));
    }
    [Fact]
    public void ReschedulingAndCancellationPreserveSnapshotsAndOrderedHistory()
    {
        var offering = new ServiceOffering { ServiceId = Area, Category = "Car", Price = 450.10m, Currency = "TRY", ActiveMinutes = 20, Employees = 1 };
        var appointment = Book(Snapshot(price: offering.Price, active: offering.ActiveMinutes));
        var initialId = appointment.Id; var initialStart = appointment.StartsAtUtc;
        var initialResources = appointment.Reservations.ToArray();
        offering.Price = 900; offering.ActiveMinutes = 60;
        appointment.Reschedule(Now.AddHours(3), [Actor], Actor, Now.AddMinutes(1));
        Assert.Equal(initialId, appointment.Id);
        Assert.Equal(450.10m, appointment.Price); Assert.Equal(20, appointment.ActiveMinutes);
        Assert.Equal("Dış yıkama", appointment.ServiceName); Assert.Equal("Car", appointment.Category);
        Assert.Equal("TRY", appointment.Currency); Assert.Equal("Europe/Istanbul", appointment.TimeZoneIdSnapshot);
        Assert.Equal(4, appointment.Reservations.Count);
        Assert.All(initialResources, x => Assert.Equal(Now.AddMinutes(1), x.ReleasedAt));
        Assert.Equal(2, appointment.Reservations.Count(x => x.ReleasedAt == null));
        Assert.Equal(initialStart, appointment.Events[1].PreviousStart);
        Assert.Equal(appointment.StartsAtUtc, appointment.Events[1].NewStart);
        appointment.Cancel(Actor, Now.AddMinutes(2));
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.All(appointment.Reservations, x => Assert.NotNull(x.ReleasedAt));
        Assert.Equal(new[] { 1, 2, 3 }, appointment.Events.Select(x => x.Sequence));
        Assert.Equal(new[] { AppointmentEventKind.Created, AppointmentEventKind.Rescheduled, AppointmentEventKind.Cancelled }, appointment.Events.Select(x => x.Kind));
        Assert.Equal(Now.AddMinutes(2), appointment.UpdatedAt);
        Assert.Equal(Now, appointment.CreatedAt);
        Assert.Equal(450.10m, appointment.Price);
        Assert.Throws<NotSupportedException>(() => ((ICollection<AppointmentEvent>)appointment.Events).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<AppointmentResourceReservation>)appointment.Reservations).Clear());
    }
    [Fact]
    public void InvalidRescheduleDoesNotChangeStateOrHistory()
    {
        var appointment = Book();
        var originalStart = appointment.StartsAtUtc;
        Assert.Throws<ArgumentException>(() => appointment.Reschedule(Now.AddHours(3), [], Actor, Now));
        Assert.Throws<ArgumentException>(() => appointment.Reschedule(Now.AddHours(3).AddSeconds(1), [Employee], Actor, Now));
        Assert.Throws<ArgumentException>(() => appointment.Reschedule(Now.AddHours(3), [Employee], Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => appointment.Reschedule(Now.AddHours(3), [Employee], Actor, Now.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(() => appointment.Reschedule(new(9999, 12, 31, 23, 50, 0, TimeSpan.Zero), [Employee], Actor, Now));
        Assert.Equal(originalStart, appointment.StartsAtUtc);
        Assert.Equal(2, appointment.Reservations.Count);
        Assert.All(appointment.Reservations, x => Assert.Null(x.ReleasedAt));
        Assert.Single(appointment.Events);
    }
    [Fact]
    public void CancelledAppointmentCannotBeCancelledAgainOrRescheduled()
    {
        var appointment = Book(); appointment.Cancel(Actor, Now);
        Assert.Throws<ArgumentException>(() => appointment.Cancel(Actor, Now));
        Assert.Throws<ArgumentException>(() => appointment.Reschedule(Now.AddHours(3), [Employee], Actor, Now));
        Assert.Equal(2, appointment.Events.Count);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
    }
    [Theory]
    [InlineData(-0.01)] [InlineData(10000000)] [InlineData(1.001)]
    public void SnapshotPriceCannotBeNegativeOutOfRangeOrRounded(double price) =>
        Assert.Throws<ArgumentException>(() => Snapshot(price: (decimal)price));
    [Theory] [InlineData(0)] [InlineData(9999999.99)]
    public void SnapshotAcceptsFreeAndMaximumExactPrices(double price) => Assert.Equal((decimal)price, Snapshot(price: (decimal)price).Price);
    [Theory]
    [InlineData(0, 1, null)] [InlineData(10081, 1, null)] [InlineData(1, 0, null)] [InlineData(1, 101, null)]
    [InlineData(1, 1, -1)] [InlineData(1, 1, 43201)]
    public void SnapshotEstimateBoundsAreEnforced(int active, int employees, int? waiting) =>
        Assert.Throws<ArgumentException>(() => Snapshot(active: active, employees: employees, waiting: waiting));
    [Fact]
    public void SnapshotUsesExistingCurrencyCategoriesAndNormalizationRules()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(currency: "USD"));
        Assert.Throws<SchedulingFault>(() => SchedulingValidation.Snapshot(Snapshot(category: "Unknown")));
        foreach (var category in WashFlow.Application.Customers.CustomerVehicleValidation.Categories)
            SchedulingValidation.Snapshot(Snapshot(category: category));
        var snapshot = Snapshot(name: "  I\u0307c\u0327 yıkama  ");
        Assert.Equal("İç yıkama", snapshot.ServiceName);
        Assert.Throws<ArgumentException>(() => Snapshot(name: "Yıkama 🚗"));
        Assert.Throws<ArgumentException>(() => Snapshot(name: new string('\u0344', 101)));
        Assert.Throws<ArgumentException>(() => Snapshot(name: new string('\ud800', 1)));
        Assert.Equal(200, Snapshot(name: new string('\u0344', 100)).ServiceName.Length);
    }
    [Theory] [InlineData("")] [InlineData("abc")] [InlineData("private request body")]
    public void RequestHashMustBeASha256HexValue(string hash) => Assert.Throws<ArgumentException>(() => Book(hash: hash));
    [Fact]
    public void UnknownSourcesAndEmptyIdentitiesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Book(source: (BookingSource)999));
        Assert.Throws<ArgumentException>(() => Appointment.Create(Guid.Empty, Actor, Employee, Area, Area, Actor, Employee,
            new string('a', 64), BookingSource.OwnerPhone, Snapshot(), Now.AddHours(2), [Employee], Now));
        Assert.Throws<ArgumentException>(() => Book(Snapshot(employees: 2)));
    }
    [Theory] [InlineData(false, false)] [InlineData(true, true)]
    public void ReservationMustReferToExactlyOneResource(bool area, bool employee) =>
        Assert.Throws<ArgumentException>(() => AppointmentResourceReservation.Create(Business, Actor, area ? Area : null,
            employee ? Employee : null, new(Now, Now.AddMinutes(20)), Now));
    [Fact]
    public void ReservationsCanBeReleasedBeforeServiceButHistoryCannotBeReactivated()
    {
        var reservation = AppointmentResourceReservation.Create(Business, Actor, null, Employee, new(Now.AddHours(1), Now.AddHours(2)), Now);
        Assert.Throws<ArgumentException>(() => reservation.Release(Now.AddTicks(-1)));
        Assert.Null(reservation.ReleasedAt);
        reservation.Release(Now.AddMinutes(1));
        Assert.Equal(Now.AddHours(1), reservation.StartsAtUtc);
        Assert.Throws<ArgumentException>(() => reservation.Release(Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(1), reservation.ReleasedAt);
        Assert.Throws<ArgumentException>(() => AppointmentResourceReservation.Create(Business, Actor, Guid.Empty, null, new(Now, Now.AddMinutes(1)), Now));
    }
    [Theory] [InlineData(0)] [InlineData(-1)]
    public void WorkAreasRequirePositiveCapacity(int capacity) => Assert.Throws<ArgumentException>(() => WorkArea.Create(Business, "İç alan", capacity, Now));
    [Fact]
    public void AreaAvailabilityAndRequirementMutationsPreserveResourceIdentity()
    {
        var area = WorkArea.Create(Business, "  I\u0307c\u0327 alan  ", 2, Now);
        Assert.Equal("İç alan", area.Name);
        var id = area.Id;
        Assert.Throws<ArgumentException>(() => area.Update("Changed", 0, Now));
        Assert.Equal(("İç alan", 2), (area.Name, area.Capacity));
        area.Update("Yeni alan", 1, Now.AddMinutes(1)); area.Archive(Now.AddMinutes(2));
        Assert.Equal(id, area.Id); Assert.Equal(Business, area.BusinessId);
        Assert.Throws<ArgumentException>(() => area.Update("Other", 1, Now.AddMinutes(3)));
        var availability = EmployeeAvailability.Create(Business, Employee, new(Now, Now.AddHours(1)), Now);
        Assert.Throws<ArgumentException>(() => availability.Update(default, Now));
        availability.Archive(Now);
        Assert.Throws<ArgumentException>(() => availability.Update(new(Now, Now.AddHours(2)), Now));
        var requirement = ServiceSchedulingRequirement.Create(Business, Area, "Car", Area, false, Now);
        Assert.Equal("unsupportedStages", Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireSingleStage(requirement)).Code);
        requirement.Update(Area, true, Now);
        SchedulingRules.RequireSingleStage(requirement);
        Assert.Equal("Car", requirement.Category);
        // No name-based guessing: a service name alone never enables a scheduling requirement.
    }
}
