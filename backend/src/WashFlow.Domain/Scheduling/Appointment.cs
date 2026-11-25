using WashFlow.Domain.Common;
namespace WashFlow.Domain.Scheduling;

public sealed class Appointment : IEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid BusinessId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid LocalCustomerId { get; private init; }
    public Guid LocalVehicleId { get; private init; }
    public Guid ServiceId { get; private init; }
    public Guid WorkAreaId { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public Guid ClientRequestId { get; private init; }
    public string RequestHash { get; private init; } = null!;
    public BookingSource Source { get; private init; }
    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Confirmed;
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset ActiveEndsAtUtc { get; private set; }
    public string TimeZoneIdSnapshot { get; private init; } = null!;
    public string ServiceName { get; private init; } = null!;
    public string CustomerName { get; private init; } = null!;
    public string VehiclePlate { get; private init; } = null!;
    public string Category { get; private init; } = null!;
    public decimal Price { get; private init; }
    public string Currency { get; private init; } = null!;
    public int ActiveMinutes { get; private init; }
    public int? WaitingMinutes { get; private init; }
    public int Employees { get; private init; }
    private readonly List<AppointmentStage> stages = [];
    public IReadOnlyList<AppointmentStage> Stages => stages.AsReadOnly();
    private readonly List<AppointmentEvent> events = [];
    private readonly List<AppointmentResourceReservation> reservations = [];
    public IReadOnlyList<AppointmentEvent> Events => events.AsReadOnly();
    public IReadOnlyList<AppointmentResourceReservation> Reservations => reservations.AsReadOnly();
    private Appointment() { }

    /// <summary>Call only after application validation and, in a future store, locked capacity/authorization checks.</summary>
    public static Appointment Create(Guid businessId, Guid customerId, Guid vehicleId, Guid serviceId, Guid workAreaId,
        Guid actor, Guid requestId, string requestHash, BookingSource source, AppointmentSnapshot snapshot,
        DateTimeOffset start, IEnumerable<Guid> employeeIds, DateTimeOffset now, IReadOnlyList<StageResourceSnapshot>? stagePlans = null, IReadOnlyList<Guid[]>? stageEmployees = null)
    {
        foreach (var id in new[] { businessId, customerId, vehicleId, serviceId, workAreaId, actor, requestId }) SchedulingInvariant.Id(id);
        ArgumentNullException.ThrowIfNull(snapshot);
        SchedulingInvariant.Require(requestHash?.Length == 64 && requestHash.All(char.IsAsciiHexDigit), "Geçerli istek özeti gereklidir.");
        SchedulingInvariant.Require(Enum.IsDefined(source), "Geçerli rezervasyon kaynağı gereklidir.");
        var employees = stagePlans == null ? SchedulingInvariant.Employees(employeeIds, snapshot.Employees) : SchedulingInvariant.EmployeePool(employeeIds, snapshot.Employees);
        var interval = Interval(start, snapshot.ActiveMinutes);
        now = now.ToUniversalTime();
        var appointment = new Appointment { BusinessId = businessId, LocalCustomerId = customerId, LocalVehicleId = vehicleId,
            ServiceId = serviceId, WorkAreaId = workAreaId, CreatedByUserId = actor, ClientRequestId = requestId,
            RequestHash = requestHash!.ToLowerInvariant(), Source = source, StartsAtUtc = interval.Start, ActiveEndsAtUtc = interval.End,
            ServiceName = snapshot.ServiceName, CustomerName = snapshot.CustomerName, VehiclePlate = snapshot.VehiclePlate,
            Category = snapshot.Category, Price = snapshot.Price, Currency = snapshot.Currency, ActiveMinutes = snapshot.ActiveMinutes,
            WaitingMinutes = snapshot.WaitingMinutes, Employees = snapshot.Employees, TimeZoneIdSnapshot = snapshot.TimeZoneId,
            CreatedAt = now, UpdatedAt = now };
        if (stagePlans != null)
        {
            SchedulingInvariant.Require(stagePlans.Count is >= 1 and <= 20, "Aşama sayısı geçersiz.");
            foreach (var plan in stagePlans) plan.Validate();
            SchedulingInvariant.Require(stagePlans.Sum(x => (long)x.Minutes) == snapshot.ActiveMinutes && stagePlans.Max(x => x.Employees) == snapshot.Employees && stagePlans[0].WorkAreaId == workAreaId,
                "Aşama gereksinimleri randevu özetiyle uyuşmalıdır.");
            var offset = 0;
            for (var i = 0; i < stagePlans.Count; i++) { appointment.stages.Add(AppointmentStage.Create(appointment, i + 1, offset, stagePlans[i], now)); offset += stagePlans[i].Minutes; }
        }
        appointment.reservations.AddRange(appointment.Reserve(interval, employees, now, stageEmployees));
        appointment.events.Add(AppointmentEvent.Create(appointment, actor, AppointmentEventKind.Created, 1, null, interval, appointment.Status, now));
        return appointment;
    }
    private static Occupancy Interval(DateTimeOffset start, int minutes)
    {
        try { return new(start, start.AddMinutes(minutes)); }
        catch (ArgumentOutOfRangeException) { throw new ArgumentException("Randevu tarih aralığı desteklenmiyor."); }
    }
    private List<AppointmentResourceReservation> Reserve(Occupancy interval, Guid[] employees, DateTimeOffset now, IReadOnlyList<Guid[]>? stageEmployees = null)
    {
        if (stages.Count == 0) return [AppointmentResourceReservation.Create(BusinessId, Id, WorkAreaId, null, interval, now),
            .. employees.Select(x => AppointmentResourceReservation.Create(BusinessId, Id, null, x, interval, now))];
        if (stageEmployees != null) SchedulingInvariant.Require(stageEmployees.Count == stages.Count,"Aşama çalışan planı eksik.");
        var rows = new List<AppointmentResourceReservation>(); var index=0;
        foreach (var stage in stages.OrderBy(x => x.Sequence))
        {
            var start = interval.Start.AddMinutes(stage.OffsetMinutes); var occupancy = new Occupancy(start, start.AddMinutes(stage.Minutes));
            rows.Add(AppointmentResourceReservation.Create(BusinessId, Id, stage.WorkAreaId, null, occupancy, now, stage.Id));
            var crew = SchedulingInvariant.Employees(stageEmployees == null ? employees.Take(stage.Employees) : stageEmployees[index],stage.Employees);
            SchedulingInvariant.Require(crew.All(employees.Contains),"Aşama çalışanları seçilen havuza ait olmalıdır.");
            rows.AddRange(crew.Select(e => AppointmentResourceReservation.Create(BusinessId, Id, null, e, occupancy, now, stage.Id))); index++;
        }
        return rows;
    }

    public void Reschedule(DateTimeOffset start, IEnumerable<Guid> employeeIds, Guid actor, DateTimeOffset now, IReadOnlyList<Guid[]>? stageEmployees = null)
    {
        SchedulingInvariant.Require(Status == AppointmentStatus.Confirmed, "Yalnızca onaylı randevu yeniden planlanabilir.");
        SchedulingInvariant.Id(actor); now = SchedulingInvariant.ChangeTime(now, UpdatedAt);
        var employees = stages.Count == 0 ? SchedulingInvariant.Employees(employeeIds, Employees) : SchedulingInvariant.EmployeePool(employeeIds,Employees);
        var interval = Interval(start, ActiveMinutes);
        var replacements = Reserve(interval, employees, now, stageEmployees);
        var change = AppointmentEvent.Create(this, actor, AppointmentEventKind.Rescheduled, checked(events.Count + 1),
            new(StartsAtUtc, ActiveEndsAtUtc), interval, Status, now);
        Release(now);
        StartsAtUtc = interval.Start; ActiveEndsAtUtc = interval.End; UpdatedAt = now;
        reservations.AddRange(replacements); events.Add(change);
    }
    public void Cancel(Guid actor, DateTimeOffset now)
    {
        SchedulingInvariant.Require(Status == AppointmentStatus.Confirmed, "Yalnızca onaylı randevu iptal edilebilir.");
        SchedulingInvariant.Id(actor); now = SchedulingInvariant.ChangeTime(now, UpdatedAt);
        var interval = new Occupancy(StartsAtUtc, ActiveEndsAtUtc);
        var change = AppointmentEvent.Create(this, actor, AppointmentEventKind.Cancelled, checked(events.Count + 1),
            interval, interval, AppointmentStatus.Cancelled, now);
        Release(now);
        Status = AppointmentStatus.Cancelled; UpdatedAt = now; events.Add(change);
    }
    private void Release(DateTimeOffset now)
    {
        var active = reservations.Where(x => x.ReleasedAt == null).ToArray();
        foreach (var reservation in active) SchedulingInvariant.ChangeTime(now, reservation.UpdatedAt);
        foreach (var reservation in active) reservation.Release(now);
    }
}
