using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WashFlow.Domain.Scheduling;
using WashFlow.Infrastructure.Persistence;
using WashFlow.Infrastructure.Scheduling;

namespace WashFlow.UnitTests;

public sealed class SchedulingPersistenceModelTests
{
    [Theory]
    [InlineData(typeof(WorkArea), "WorkAreas")]
    [InlineData(typeof(ServiceSchedulingRequirement), "ServiceSchedulingRequirements")]
    [InlineData(typeof(EmployeeAvailability), "EmployeeAvailabilities")]
    [InlineData(typeof(Appointment), "Appointments")]
    [InlineData(typeof(AppointmentEvent), "AppointmentEvents")]
    [InlineData(typeof(AppointmentResourceReservation), "AppointmentResourceReservations")]
    public void SchedulingEntitiesHaveTenantKeysXminAndUtcMappings(Type type, string table)
    {
        using var db = new WashFlowDbContextFactory().CreateDbContext([]);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(type)!;
        Assert.Equal(table, entity.GetTableName());
        Assert.Contains(entity.GetKeys(), k => k.Properties.Select(x => x.Name).SequenceEqual(new[] { "BusinessId", "Id" }));
        var version = entity.FindProperty("Version")!;
        Assert.True(version.IsConcurrencyToken); Assert.Equal("xmin", version.GetColumnName());
        foreach (var p in entity.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            Assert.Equal("timestamp with time zone", p.GetColumnType());
        Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
    }
    [Fact]
    public void AppointmentHasImmutableSnapshotPrecisionTenantOwnershipAndBusinessIdempotencyKeys()
    {
        using var db = new WashFlowDbContextFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;
        var a = model.FindEntityType(typeof(Appointment))!;
        Assert.Equal(9, a.FindProperty("Price")!.GetPrecision()); Assert.Equal(2, a.FindProperty("Price")!.GetScale());
        Assert.Contains(a.GetIndexes(), i => i.IsUnique && i.Properties.Select(x => x.Name).SequenceEqual(new[] { "BusinessId", "ClientRequestId" }));
        Assert.Contains(a.GetForeignKeys(), k => k.Properties.Select(x => x.Name).SequenceEqual(new[] { "BusinessId", "LocalCustomerId", "LocalVehicleId" }));
        Assert.Equal("events", a.FindNavigation("Events")!.FieldInfo!.Name);
        Assert.Equal("reservations", a.FindNavigation("Reservations")!.FieldInfo!.Name);
        var receipt = model.FindEntityType(typeof(SchedulingCreateReceipt))!;
        Assert.Equal(new[] { "BusinessId", "ClientRequestId" }, receipt.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal("jsonb", receipt.FindProperty("ResultJson")!.GetColumnType());
    }
    [Fact]
    public void ResourceIndexesSupportActiveOverlapQueriesAndUniqueResources()
    {
        using var db = new WashFlowDbContextFactory().CreateDbContext([]);
        var r = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AppointmentResourceReservation))!;
        Assert.Equal(2, r.GetIndexes().Count(i => i.IsUnique && i.GetFilter()!.Contains("ReleasedAt")));
        Assert.Equal(2, r.GetIndexes().Count(i => !i.IsUnique && i.Properties.Any(p => p.Name == "EndsAtUtc") && i.GetFilter()!.Contains("ReleasedAt")));
        Assert.Contains(r.GetCheckConstraints(), c => c.Name!.EndsWith("_Resource", StringComparison.Ordinal));
        Assert.Contains(r.GetCheckConstraints(), c => c.Name!.EndsWith("_Interval", StringComparison.Ordinal));
    }
}
