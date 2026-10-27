using WashFlow.Application.Businesses;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Scheduling;

namespace WashFlow.UnitTests;

public sealed class SchedulingRulesTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
    private static Occupancy Slot(int start, int end) => new(Day.AddMinutes(start), Day.AddMinutes(end));
    private static IReadOnlyList<OpeningDay> Hours(int open = 540, int close = 1080) =>
        Enumerable.Range(1, 7).Select(day => new OpeningDay(day, false, open, close)).ToArray();

    [Theory]
    [InlineData(0, 20, 20, 40, false)]
    [InlineData(20, 40, 0, 20, false)]
    [InlineData(0, 20, 19, 40, true)]
    [InlineData(0, 20, 0, 20, true)]
    [InlineData(0, 60, 20, 40, true)]
    [InlineData(0, 20, 40, 60, false)]
    public void OverlapIsSymmetricAndHalfOpen(int a, int b, int c, int d, bool expected)
    {
        Assert.Equal(expected, SchedulingRules.Overlaps(Slot(a, b), Slot(c, d)));
        Assert.Equal(expected, SchedulingRules.Overlaps(Slot(c, d), Slot(a, b)));
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(20, 0)]
    public void EmptyAndReversedIntervalsAreRejected(int start, int end) =>
        Assert.Throws<ArgumentException>(() => Slot(start, end));
    [Fact]
    public void OffsetInputsBecomeUtcAndMustHaveMinutePrecision()
    {
        var start = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.FromHours(3));
        var interval = new Occupancy(start, start.AddMinutes(20));
        Assert.Equal(TimeSpan.Zero, interval.Start.Offset);
        Assert.Equal(Day.AddHours(7), interval.Start);
        Assert.Throws<ArgumentException>(() => new Occupancy(start.AddSeconds(1), start.AddMinutes(20)));
        Assert.Throws<ArgumentException>(() => new Occupancy(start, start.AddMinutes(20).AddTicks(1)));
    }
    [Fact]
    public void DefaultIntervalsCannotBypassAnyCalculation()
    {
        Assert.Throws<ArgumentException>(() => SchedulingRules.Overlaps(default, Slot(0, 20)));
        Assert.Throws<ArgumentException>(() => SchedulingRules.Peak([default], Slot(0, 20)));
        Assert.Throws<ArgumentException>(() => SchedulingRules.Peak([], default(Occupancy)));
        Assert.Throws<ArgumentException>(() => SchedulingRules.Covered([], default));
        Assert.Throws<ArgumentException>(() => SchedulingRules.Covered([default], Slot(0, 20)));
        Assert.Throws<ArgumentException>(() => SchedulingRules.EmployeeAvailable([], [default], Slot(0, 20)));
        Assert.Throws<ArgumentException>(() => SchedulingRules.WithinHours(default, TimeZoneInfo.Utc, Hours()));
    }
    [Theory]
    [InlineData(1, false)] [InlineData(2, true)]
    public void CountsPeakRatherThanAllOverlappingRows(int capacity, bool available)
    {
        var existing = new[] { Slot(540, 560), Slot(560, 580) };
        var request = Slot(550, 570);
        Assert.Equal(2, SchedulingRules.Peak(existing, request));
        Assert.Equal(available, SchedulingRules.HasCapacity(existing, request, capacity));
    }
    [Fact]
    public void CapacityTwoRejectsThirdConcurrentVehicleButAcceptsAdjacency()
    {
        var existing = new[] { Slot(0, 20), Slot(0, 20) };
        Assert.False(SchedulingRules.HasCapacity(existing, Slot(10, 30), 2));
        Assert.True(SchedulingRules.HasCapacity(existing, Slot(20, 40), 2));
        Assert.Equal(1, SchedulingRules.Peak(existing, Slot(40, 60)));
        Assert.Equal(0, SchedulingRules.Peak([]));
        Assert.Equal(2, SchedulingRules.Peak(existing));
    }
    [Fact]
    public void PeakIsIndependentOfInputOrderAndClipsOutsideLoad()
    {
        var existing = new[] { Slot(0, 100), Slot(0, 20), Slot(20, 40), Slot(60, 80) };
        Assert.Equal(SchedulingRules.Peak(existing), SchedulingRules.Peak(existing.Reverse()));
        Assert.Equal(2, SchedulingRules.Peak(existing, Slot(40, 60)));
    }
    [Theory] [InlineData(0)] [InlineData(-1)]
    public void NonpositiveCapacityHasSafeValidationError(int capacity)
    {
        var error = Assert.Throws<SchedulingFault>(() => SchedulingRules.HasCapacity([], Slot(0, 20), capacity));
        Assert.Equal((400, "invalidCapacity"), (error.Status, error.Code));
    }
    [Fact]
    public void AvailabilityRequiresCompleteCoverageAndNeverBridgesBreaks()
    {
        Assert.False(SchedulingRules.Covered([], Slot(10, 20)));
        Assert.True(SchedulingRules.Covered([Slot(20, 40), Slot(0, 20)], Slot(10, 30)));
        Assert.True(SchedulingRules.Covered([Slot(0, 30), Slot(20, 40)], Slot(0, 40)));
        Assert.False(SchedulingRules.Covered([Slot(0, 20), Slot(21, 40)], Slot(10, 30)));
        Assert.False(SchedulingRules.Covered([Slot(11, 30)], Slot(10, 20)));
        Assert.False(SchedulingRules.Covered([Slot(0, 19)], Slot(10, 20)));
    }
    [Fact]
    public void EmployeeAvailabilityAndConflictsAreBothRequired()
    {
        var request = Slot(20, 40);
        Assert.True(SchedulingRules.EmployeeAvailable([Slot(0, 60)], [Slot(0, 20), Slot(40, 60)], request));
        Assert.False(SchedulingRules.EmployeeAvailable([Slot(0, 60)], [Slot(39, 60)], request));
        Assert.False(SchedulingRules.EmployeeAvailable([], [], request));
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(101)]
    public void RequiredEmployeeCountMustBePositiveAndBounded(int required)
    {
        Assert.Equal("invalidEmployees", Assert.Throws<SchedulingFault>(() => SchedulingRules.Employees([], required)).Code);
    }
    [Fact]
    public void EmployeeSelectionMustMatchExactlyAndContainDistinctNonemptyIds()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Assert.Equal(new[] { a, b }, SchedulingRules.Employees([b, a], 2));
        foreach (var ids in new IReadOnlyList<Guid>?[] { null, [], [a], [a, a], [a, Guid.Empty], [a, b, Guid.NewGuid()] })
            Assert.Throws<SchedulingFault>(() => SchedulingRules.Employees(ids, 2));
    }
    [Theory]
    [InlineData(540, 1080, true)]
    [InlineData(539, 560, false)]
    [InlineData(1070, 1081, false)]
    [InlineData(1080, 1100, false)]
    [InlineData(540, 1980, false)]
    public void ActiveIntervalMustFitOneOpeningInterval(int start, int end, bool expected) =>
        Assert.Equal(expected, SchedulingRules.WithinHours(Slot(start, end), TimeZoneInfo.Utc, Hours()));
    [Fact]
    public void ClosingAt2400IsTheExclusiveNextMidnightBoundary()
    {
        Assert.True(SchedulingRules.WithinHours(Slot(1420, 1440), TimeZoneInfo.Utc, Hours(0, 1440)));
        Assert.False(SchedulingRules.WithinHours(Slot(1420, 1441), TimeZoneInfo.Utc, Hours(0, 1440)));
        Assert.True(SchedulingRules.WithinHours(Slot(0, 1440), TimeZoneInfo.Utc, Hours(0, 1440)));
    }
    [Fact]
    public void MissingClosedAndMalformedHoursFailClosed()
    {
        var request = Slot(600, 620);
        Assert.False(SchedulingRules.WithinHours(request, TimeZoneInfo.Utc, null));
        Assert.False(SchedulingRules.WithinHours(request, TimeZoneInfo.Utc, []));
        Assert.False(SchedulingRules.WithinHours(request, TimeZoneInfo.Utc, Enumerable.Repeat(Hours()[0], 7).ToArray()));
        Assert.False(SchedulingRules.WithinHours(request, TimeZoneInfo.Utc, [.. Hours().Take(6), null!]));
        Assert.False(SchedulingRules.WithinHours(request, TimeZoneInfo.Utc, Hours(100, 100)));
        Assert.False(SchedulingRules.WithinHours(request, TimeZoneInfo.Utc,
            Enumerable.Range(1, 7).Select(x => new OpeningDay(x, true, null, null)).ToArray()));
    }
    [Fact]
    public void IstanbulHoursUseBusinessTimezoneRatherThanUtcOrMachineLocal()
    {
        var zone = SchedulingRules.Zone("Europe/Istanbul");
        Assert.Equal(Day.AddHours(6), SchedulingRules.Resolve("2026-10-27T09:00", 180, zone));
        Assert.True(SchedulingRules.WithinHours(Slot(360, 380), zone, Hours()));
        Assert.False(SchedulingRules.WithinHours(Slot(350, 370), zone, Hours()));
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("Turkey Standard Time")] [InlineData("Not/AZone")]
    public void InvalidTimezoneDoesNotFallBackToMachineLocal(string? id) =>
        Assert.Throws<SchedulingFault>(() => SchedulingRules.Zone(id));
    [Theory]
    [InlineData(null)] [InlineData("2026-10-27T10:00:00")] [InlineData("2026-10-27T24:00")]
    [InlineData("2026-02-30T10:00")] [InlineData("2026-10-27T10:00Z")]
    public void LocalInputMustBeAnExactExistingMinute(string? local) =>
        Assert.Equal("invalidLocalTime", Assert.Throws<SchedulingFault>(() => SchedulingRules.Resolve(local, null, TimeZoneInfo.Utc)).Code);
    [Fact]
    public void DstGapIsRejectedAndFoldRequiresAnExplicitValidOffset()
    {
        var zone = SchedulingRules.Zone("America/New_York");
        Assert.Equal("invalidLocalTime", Assert.Throws<SchedulingFault>(() => SchedulingRules.Resolve("2026-03-08T02:30", -300, zone)).Code);
        Assert.Equal("ambiguousLocalTime", Assert.Throws<SchedulingFault>(() => SchedulingRules.Resolve("2026-11-01T01:30", null, zone)).Code);
        var earlier = SchedulingRules.Resolve("2026-11-01T01:30", -240, zone);
        var later = SchedulingRules.Resolve("2026-11-01T01:30", -300, zone);
        Assert.Equal(TimeSpan.FromHours(1), later - earlier);
        Assert.Equal(TimeSpan.Zero, earlier.Offset);
        Assert.Throws<SchedulingFault>(() => SchedulingRules.Resolve("2026-11-01T01:30", 0, zone));
    }
    [Fact]
    public void TransitionBoundariesAndUnrepresentableDatesFailSafely()
    {
        var zone = SchedulingRules.Zone("America/New_York");
        var start = SchedulingRules.Resolve("2026-11-01T01:30", -240, zone);
        Assert.False(SchedulingRules.WithinHours(new(start, start.AddMinutes(20)), zone, Hours(60, 180)));
        Assert.Equal("invalidLocalTime", Assert.Throws<SchedulingFault>(() =>
            SchedulingRules.Resolve("0001-01-01T00:00", 180, TimeZoneInfo.CreateCustomTimeZone("Test/Plus3", TimeSpan.FromHours(3), "Test", "Test"))).Code);
        Assert.False(SchedulingRules.WithinHours(new(new(9999, 12, 31, 23, 0, 0, TimeSpan.Zero), new(9999, 12, 31, 23, 30, 0, TimeSpan.Zero)),
            TimeZoneInfo.Utc, Hours(0, 1440)));
    }
    [Fact]
    public void VersionsAreOpaquePositiveValuesAndNeverSilentlyRefreshed()
    {
        SchedulingRules.RequireVersion(uint.MaxValue, uint.MaxValue);
        Assert.Equal(400, Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireVersion(1, 0)).Status);
        Assert.Equal("scheduleConflict", Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireVersion(2, 1)).Code);
        Assert.Throws<SchedulingFault>(() => SchedulingRules.RequireVersion(0, 1));
    }
}
