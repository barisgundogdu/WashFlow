using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.WorkOrders;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Scheduling;
using WashFlow.Domain.Identity;
namespace WashFlow.Infrastructure.Persistence.Configurations;

public sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>, IEntityTypeConfiguration<WorkOrderStage>, IEntityTypeConfiguration<WorkStageAssignment>, IEntityTypeConfiguration<WorkOrderEvent>
{
    public void Configure(EntityTypeBuilder<WorkOrder> b)
    {
        b.ConfigureEntity("WorkOrders"); b.HasAlternateKey(x => new { x.BusinessId, x.Id });
        b.HasIndex(x => new { x.BusinessId, x.AppointmentId }).IsUnique().HasFilter("\"CancelledAtUtc\" IS NULL");
        b.Property(x => x.CreationHash).HasMaxLength(64).IsRequired();
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => new { x.BusinessId, x.AppointmentId }).HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Stages).WithOne().HasForeignKey(x => new { x.BusinessId, x.WorkOrderId }).HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => new { x.BusinessId, x.WorkOrderId }).HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Stages).HasField("stages").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Events).HasField("events").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.ToTable("WorkOrders", t => t.HasCheckConstraint("CK_WorkOrders_Lifecycle", "NOT (\"CompletedAtUtc\" IS NOT NULL AND \"CancelledAtUtc\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NULL OR \"CompletedAtUtc\" >= \"CreatedAt\") AND (\"CancelledAtUtc\" IS NULL OR \"CancelledAtUtc\" >= \"CreatedAt\")"));
    }
    public void Configure(EntityTypeBuilder<WorkOrderStage> b)
    {
        b.ConfigureEntity("WorkOrderStages"); b.HasAlternateKey(x => new { x.BusinessId, x.Id });
        b.HasAlternateKey(x => new { x.BusinessId, x.WorkOrderId, x.Id });
        b.Property(x => x.Name).HasMaxLength(100).IsRequired(); b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.BusinessId, x.WorkOrderId, x.Sequence }).IsUnique();
        b.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => new { x.BusinessId, x.StageId }).HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Assignments).HasField("assignments").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.ToTable("WorkOrderStages", t => t.HasCheckConstraint("CK_WorkOrderStages_State", "\"Sequence\" > 0 AND length(btrim(\"Name\")) > 0 AND \"EstimatedMinutes\" BETWEEN 1 AND 10080 AND \"Status\" BETWEEN 1 AND 6 AND \"PausedMilliseconds\" >= 0 AND ((\"Status\" IN (1,2) AND \"StartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"PausedAtUtc\" IS NULL) OR (\"Status\" = 6 AND \"CompletedAtUtc\" IS NULL AND \"PausedAtUtc\" IS NULL) OR (\"Status\" = 3 AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND \"PausedAtUtc\" IS NULL) OR (\"Status\" = 4 AND \"StartedAtUtc\" IS NOT NULL AND \"PausedAtUtc\" IS NOT NULL AND \"PausedAtUtc\" >= \"StartedAtUtc\" AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" = 5 AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"StartedAtUtc\" AND \"PausedAtUtc\" IS NULL))"));
    }
    public void Configure(EntityTypeBuilder<WorkStageAssignment> b)
    {
        b.ToTable("WorkStageAssignments"); b.HasKey(x => new { x.BusinessId, x.StageId, x.EmployeeMembershipId });
        b.HasOne<BusinessMembership>().WithMany().HasForeignKey(x => new { x.BusinessId, x.EmployeeMembershipId }).HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BusinessId, x.EmployeeMembershipId, x.StageId });
    }
    public void Configure(EntityTypeBuilder<WorkOrderEvent> b)
    {
        b.ToTable("WorkOrderEvents"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.BusinessId, x.RequestId }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.WorkOrderId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkOrderStage>().WithMany().HasForeignKey(x => new { x.BusinessId, x.WorkOrderId, x.StageId }).HasPrincipalKey(x => new { x.BusinessId, x.WorkOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("WorkOrderEvents", t => t.HasCheckConstraint("CK_WorkOrderEvents_Action", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Action\" IN ('create','start','pause','resume','complete','cancel')"));
    }
}
