using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Common;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Domain.Scheduling;
using WashFlow.Infrastructure.Scheduling;

namespace WashFlow.Infrastructure.Persistence.Configurations;

public sealed class SchedulingConfiguration : IEntityTypeConfiguration<WorkArea>,
    IEntityTypeConfiguration<ServiceSchedulingRequirement>, IEntityTypeConfiguration<EmployeeAvailability>,
    IEntityTypeConfiguration<Appointment>, IEntityTypeConfiguration<AppointmentEvent>,
    IEntityTypeConfiguration<AppointmentResourceReservation>, IEntityTypeConfiguration<SchedulingCreateReceipt>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table) where T : class, IEntity
    {
        b.ConfigureEntity(table);
        b.HasAlternateKey("BusinessId", "Id");
        b.HasOne<Business>().WithMany().HasForeignKey("BusinessId").OnDelete(DeleteBehavior.Restrict);
        // Explicit names/types, including nullable instants. All intervals are stored as UTC timestamptz.
        foreach (var p in b.Metadata.GetProperties().Where(p => p.Name != "Version"))
        {
            b.Property(p.Name).HasColumnName(p.Name);
            if (p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?))
                b.Property(p.Name).HasColumnType("timestamp with time zone");
        }
    }
    private static void Interval<T>(EntityTypeBuilder<T> b, string table, string end = "EndsAtUtc") where T : class =>
        b.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_Interval",
            $"\"StartsAtUtc\" < \"{end}\" AND date_trunc('minute', \"StartsAtUtc\") = \"StartsAtUtc\" AND date_trunc('minute', \"{end}\") = \"{end}\""));
    private static void Archive<T>(EntityTypeBuilder<T> b, string table) where T : class =>
        b.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_Archive", "\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\""));
    public void Configure(EntityTypeBuilder<WorkArea> b)
    {
        Base(b, "WorkAreas"); b.NonBlank("WorkAreas", "Name", 200); Archive(b, "WorkAreas");
        b.ToTable("WorkAreas", t => t.HasCheckConstraint("CK_WorkAreas_Capacity", "\"Capacity\" > 0"));
        b.HasIndex(x => new { x.BusinessId, x.Name, x.Id });
    }
    public void Configure(EntityTypeBuilder<ServiceSchedulingRequirement> b)
    {
        Base(b, "ServiceSchedulingRequirements"); b.Property(x => x.Category).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.BusinessId, x.ServiceId, x.Category }).IsUnique();
        b.HasOne<BusinessService>().WithMany().HasForeignKey(x => new { x.BusinessId, x.ServiceId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        // Stable service/category identity is validated by the application. No offering FK:
        // Phase 8 replaces offering rows, and removing an offering must preserve history/configuration.
        b.ToTable("ServiceSchedulingRequirements", t => t.HasCheckConstraint("CK_ServiceSchedulingRequirements_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')"));
        b.HasOne<WorkArea>().WithMany().HasForeignKey(x => new { x.BusinessId, x.WorkAreaId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<EmployeeAvailability> b)
    {
        Base(b, "EmployeeAvailabilities"); Interval(b, "EmployeeAvailabilities"); Archive(b, "EmployeeAvailabilities");
        b.HasOne<BusinessMembership>().WithMany().HasForeignKey(x => new { x.BusinessId, x.MembershipId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BusinessId, x.MembershipId, x.StartsAtUtc, x.EndsAtUtc }).HasFilter("\"ArchivedAt\" IS NULL");
    }
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        Base(b, "Appointments"); Interval(b, "Appointments", "ActiveEndsAtUtc");
        b.Property(x => x.Source).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>();
        foreach (var (name, length) in new[] { ("TimeZoneIdSnapshot",100),("ServiceName",200),("CustomerName",200),("VehiclePlate",32),("Category",32),("Currency",3),("RequestHash",64) })
            b.NonBlank("Appointments", name, length);
        b.Property(x => x.Price).HasPrecision(9, 2);
        b.HasIndex(x => new { x.BusinessId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.StartsAtUtc, x.Id });
        b.HasIndex(x => new { x.BusinessId, x.Status, x.StartsAtUtc });
        b.HasOne<LocalCustomer>().WithMany().HasForeignKey(x => new { x.BusinessId, x.LocalCustomerId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LocalVehicle>().WithMany().HasForeignKey(x => new { x.BusinessId, x.LocalCustomerId, x.LocalVehicleId })
            .HasPrincipalKey(x => new { x.BusinessId, x.LocalCustomerId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessService>().WithMany().HasForeignKey(x => new { x.BusinessId, x.ServiceId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkArea>().WithMany().HasForeignKey(x => new { x.BusinessId, x.WorkAreaId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => new { x.BusinessId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Reservations).WithOne().HasForeignKey(x => new { x.BusinessId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Events).HasField("events").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Reservations).HasField("reservations").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.ToTable("Appointments", t => {
            t.HasCheckConstraint("CK_Appointments_State", "\"Status\" IN (1,2) AND \"Source\" = 1");
            t.HasCheckConstraint("CK_Appointments_Hash", "\"RequestHash\" ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("CK_Appointments_Snapshot", "\"Price\" BETWEEN 0 AND 9999999.99 AND \"Currency\" = 'TRY' AND \"ActiveMinutes\" BETWEEN 1 AND 10080 AND (\"WaitingMinutes\" IS NULL OR \"WaitingMinutes\" BETWEEN 0 AND 43200) AND \"Employees\" BETWEEN 1 AND 100 AND \"ActiveEndsAtUtc\" = \"StartsAtUtc\" + \"ActiveMinutes\" * interval '1 minute'");
        });
    }
    public void Configure(EntityTypeBuilder<AppointmentEvent> b)
    {
        Base(b, "AppointmentEvents"); b.Property(x => x.Kind).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.BusinessId, x.AppointmentId, x.Sequence }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("AppointmentEvents", t => t.HasCheckConstraint("CK_AppointmentEvents_State",
            "\"Sequence\" > 0 AND \"Kind\" IN (1,2,3) AND \"Status\" IN (1,2) AND \"NewStart\" < \"NewEnd\" AND ((\"PreviousStart\" IS NULL AND \"PreviousEnd\" IS NULL) OR (\"PreviousStart\" IS NOT NULL AND \"PreviousEnd\" IS NOT NULL AND \"PreviousStart\" < \"PreviousEnd\"))"));
    }
    public void Configure(EntityTypeBuilder<AppointmentResourceReservation> b)
    {
        Base(b, "AppointmentResourceReservations"); Interval(b, "AppointmentResourceReservations");
        b.HasOne<WorkArea>().WithMany().HasForeignKey(x => new { x.BusinessId, x.WorkAreaId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessMembership>().WithMany().HasForeignKey(x => new { x.BusinessId, x.EmployeeMembershipId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("AppointmentResourceReservations", t => {
            t.HasCheckConstraint("CK_AppointmentResourceReservations_Resource", "(\"WorkAreaId\" IS NULL) <> (\"EmployeeMembershipId\" IS NULL)");
            t.HasCheckConstraint("CK_AppointmentResourceReservations_Release", "\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"CreatedAt\"");
        });
        b.HasIndex(x => new { x.BusinessId, x.WorkAreaId, x.StartsAtUtc, x.EndsAtUtc }).HasFilter("\"ReleasedAt\" IS NULL AND \"WorkAreaId\" IS NOT NULL");
        b.HasIndex(x => new { x.BusinessId, x.EmployeeMembershipId, x.StartsAtUtc, x.EndsAtUtc }).HasFilter("\"ReleasedAt\" IS NULL AND \"EmployeeMembershipId\" IS NOT NULL");
        b.HasIndex(x => new { x.BusinessId, x.AppointmentId, x.EmployeeMembershipId }).IsUnique().HasFilter("\"ReleasedAt\" IS NULL AND \"EmployeeMembershipId\" IS NOT NULL");
        b.HasIndex(x => new { x.BusinessId, x.AppointmentId, x.WorkAreaId }).IsUnique().HasFilter("\"ReleasedAt\" IS NULL AND \"WorkAreaId\" IS NOT NULL");
    }
    public void Configure(EntityTypeBuilder<SchedulingCreateReceipt> b)
    {
        b.ToTable("SchedulingCreateReceipts"); b.HasKey(x => new { x.BusinessId, x.ClientRequestId });
        b.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => new { x.BusinessId, x.AppointmentId }).IsUnique();
        b.HasOne<Appointment>().WithOne().HasForeignKey<SchedulingCreateReceipt>(x => new { x.BusinessId, x.AppointmentId })
            .HasPrincipalKey<Appointment>(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
