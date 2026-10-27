using System.Globalization;
using System.Text.Json;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Scheduling;

namespace WashFlow.UnitTests;

public sealed class SchedulingContractsTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Employee = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);
    private static BookingInput Input() => new(Id, Id, Id, Id, 1, 1, "2026-10-27T09:00", 180, [Id, Employee]);

    [Fact]
    public void CanonicalRequestIdentityIgnoresEmployeeOrderButDetectsDifferentRequests()
    {
        var input = Input(); var zone = SchedulingRules.Zone("Europe/Istanbul");
        var hash = SchedulingValidation.RequestHash(input, zone);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, SchedulingValidation.RequestHash(input with { EmployeeMembershipIds = [Employee, Id] }, zone));
        Assert.Equal(hash, SchedulingValidation.RequestHash(input with { UtcOffsetMinutes = null }, zone));
        Assert.NotEqual(hash, SchedulingValidation.RequestHash(input with { StartLocal = "2026-10-27T09:01" }, zone));
        Assert.NotEqual(hash, SchedulingValidation.RequestHash(input with { ExpectedServiceVersion = 2 }, zone));
        Assert.NotEqual(hash, SchedulingValidation.RequestHash(input with { LocalVehicleId = Employee }, zone));
        // Request IDs are part of the future unique lookup key, not the canonical payload digest.
        Assert.Equal(hash, SchedulingValidation.RequestHash(input with { ClientRequestId = Employee }, zone));
        var fold = input with { StartLocal = "2026-11-01T01:30", UtcOffsetMinutes = -240 };
        var ny = SchedulingRules.Zone("America/New_York");
        Assert.NotEqual(SchedulingValidation.RequestHash(fold, ny), SchedulingValidation.RequestHash(fold with { UtcOffsetMinutes = -300 }, ny));
    }
    [Fact]
    public void CanonicalHashIsCultureIndependentAndDoesNotMutateInput()
    {
        var original = CultureInfo.CurrentCulture;
        var ids = new List<Guid> { Employee, Id }; var input = Input() with { EmployeeMembershipIds = ids };
        var zone = SchedulingRules.Zone("Europe/Istanbul"); var hash = SchedulingValidation.RequestHash(input, zone);
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); Assert.Equal(hash, SchedulingValidation.RequestHash(input, zone)); }
        finally { CultureInfo.CurrentCulture = original; }
        Assert.Equal(new[] { Employee, Id }, ids);
    }
    [Theory] [InlineData("Customer")] [InlineData("WalkIn")] [InlineData("OwnerManual")] [InlineData("1")] [InlineData("")]
    public void UnapprovedSourcesCannotEnterAValidatedRequest(string source) =>
        Assert.Contains("source", SchedulingValidation.Booking(Input() with { Source = source }).Keys);
    [Fact]
    public void BookingRequiresIdsVersionsTimeAndDistinctEmployees()
    {
        Assert.Empty(SchedulingValidation.Booking(Input()));
        var invalid = Input() with { ClientRequestId = Guid.Empty, LocalCustomerId = Guid.Empty, LocalVehicleId = Guid.Empty,
            ServiceId = Guid.Empty, ExpectedServiceVersion = 0, ExpectedVehicleVersion = 0, StartLocal = "tomorrow", EmployeeMembershipIds = [], UtcOffsetMinutes = 841 };
        Assert.Equal(9, SchedulingValidation.Booking(invalid).Count);
        Assert.Throws<SchedulingFault>(() => SchedulingValidation.RequestHash(invalid, TimeZoneInfo.Utc));
        Assert.Contains("employeeMembershipIds", SchedulingValidation.Booking(Input() with { EmployeeMembershipIds = [Id, Id] }).Keys);
    }
    [Fact]
    public void ResourceContractsValidateNormalizedNamesVersionsAndIntervals()
    {
        Assert.Empty(SchedulingValidation.WorkArea(new(new string('\u0344', 100), 2, null), false));
        Assert.Contains("name", SchedulingValidation.WorkArea(new(new string('\u0344', 101), 2, null), false).Keys);
        Assert.Contains("capacity", SchedulingValidation.WorkArea(new("Alan", 0, null), false).Keys);
        Assert.Contains("version", SchedulingValidation.WorkArea(new("Alan", 2, null), true).Keys);
        Assert.Empty(SchedulingValidation.WorkArea(new("Alan", 2, uint.MaxValue), true));
        Assert.Empty(SchedulingValidation.Availability(new(Now, Now.AddMinutes(20), 1), true));
        Assert.Contains("interval", SchedulingValidation.Availability(new(Now, Now, 1), true).Keys);
        Assert.Contains("interval", SchedulingValidation.Availability(new(Now.AddSeconds(1), Now.AddMinutes(20), 1), true).Keys);
        Assert.Contains("version", SchedulingValidation.Availability(new(Now, Now.AddMinutes(20), 0), true).Keys);
        Assert.Empty(SchedulingValidation.Requirement(new(Id, false, null), "Car", false));
        Assert.Equal(3, SchedulingValidation.Requirement(new(Guid.Empty, false, null), "unknown", true).Count);
    }
    [Fact]
    public void ReschedulingRequiresVersionAndMinuteTimeButDoesNotAcceptNewCommercialValues()
    {
        Assert.Empty(SchedulingValidation.Reschedule(new(1, "2026-10-27T10:00", 180, [Id])));
        Assert.Equal(4, SchedulingValidation.Reschedule(new(0, "bad", -841, [])).Count);
        var propertyNames = typeof(RescheduleInput).GetProperties().Select(x => x.Name);
        Assert.DoesNotContain("Price", propertyNames);
        Assert.DoesNotContain("ServiceId", propertyNames);
        Assert.DoesNotContain("LocalVehicleId", propertyNames);
    }
    [Fact]
    public void PreviewAndVersionedCommandsAreValidatedBeforeFuturePersistence()
    {
        Assert.Empty(SchedulingValidation.Versioned(new(uint.MaxValue)));
        Assert.Contains("version", SchedulingValidation.Versioned(new(0)).Keys);
        Assert.Empty(SchedulingValidation.AvailabilityCheck(new(Id, "Car", "2026-10-27T09:00", 180, [Employee])));
        Assert.Equal(4, SchedulingValidation.AvailabilityCheck(new(Guid.Empty, "Unknown", "bad", null, [])).Count);
        var from = new DateOnly(2026, 10, 27);
        Assert.Contains("status", SchedulingValidation.Calendar(new(from, from.AddDays(1), Status: (AppointmentStatus)999)).Keys);
    }
    [Theory]
    [InlineData(0, 1, 20, false)] [InlineData(-1, 1, 20, false)] [InlineData(32, 1, 20, false)]
    [InlineData(1, 0, 20, false)] [InlineData(1, 10001, 20, false)] [InlineData(1, 1, 0, false)]
    [InlineData(1, 1, 51, false)] [InlineData(31, 10000, 50, true)]
    public void CalendarRangeAndPaginationAreBounded(int days, int page, int size, bool valid)
    {
        var from = new DateOnly(2026, 10, 27);
        Assert.Equal(valid, SchedulingValidation.Calendar(new(from, from.AddDays(days), page, size)).Count == 0);
    }
    [Fact]
    public void AppointmentResponseUsesExactPriceStringAndDetachedHistory()
    {
        var snapshot = new AppointmentSnapshot("Yıkama", "Ayşe", "34 AB 123", "Car", 450.10m, "TRY", 20, null, 1, "Etc/UTC");
        var appointment = Appointment.Create(Id, Id, Id, Id, Id, Id, Id, new string('a', 64), BookingSource.OwnerPhone, snapshot, Now.AddHours(1), [Employee], Now);
        var view = SchedulingValidation.View(appointment, uint.MaxValue);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("price").ValueKind);
        Assert.Equal("450.10", json.RootElement.GetProperty("price").GetString());
        Assert.Equal("OwnerPhone", json.RootElement.GetProperty("source").GetString());
        Assert.Equal("Confirmed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Created", json.RootElement.GetProperty("events")[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("waitingMinutes").ValueKind);
        Assert.False(json.RootElement.TryGetProperty("requestHash", out _));
        appointment.Cancel(Id, Now);
        Assert.Equal(AppointmentStatus.Confirmed, view.Status);
        Assert.Single(view.Events);
        Assert.All(view.Reservations, x => Assert.Null(x.ReleasedAt));
        Assert.Throws<SchedulingFault>(() => SchedulingValidation.View(appointment, 0));
    }
}
