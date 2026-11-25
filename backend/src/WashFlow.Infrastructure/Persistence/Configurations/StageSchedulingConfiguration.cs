using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Scheduling;
using WashFlow.Domain.Catalog;
namespace WashFlow.Infrastructure.Persistence.Configurations;

public sealed class StageSchedulingConfiguration : IEntityTypeConfiguration<ServiceStageTemplate>, IEntityTypeConfiguration<AppointmentStage>
{
    public void Configure(EntityTypeBuilder<ServiceStageTemplate> b)
    {
        b.ConfigureEntity("ServiceStageTemplates"); b.HasAlternateKey(x => new { x.BusinessId,x.Id });
        b.Property(x=>x.Category).HasMaxLength(32).IsRequired(); b.Property(x=>x.Name).HasMaxLength(100).IsRequired();
        b.HasOne<BusinessService>().WithMany().HasForeignKey(x=>new{x.BusinessId,x.ServiceId}).HasPrincipalKey(x=>new{x.BusinessId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkArea>().WithMany().HasForeignKey(x=>new{x.BusinessId,x.WorkAreaId}).HasPrincipalKey(x=>new{x.BusinessId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.BusinessId,x.ServiceId,x.Category,x.Sequence}).IsUnique().HasFilter("\"ArchivedAt\" IS NULL");
        b.ToTable("ServiceStageTemplates",t=>t.HasCheckConstraint("CK_ServiceStageTemplates_Plan","\"Sequence\" BETWEEN 1 AND 20 AND length(btrim(\"Name\")) BETWEEN 1 AND 100 AND \"Minutes\" BETWEEN 1 AND 10080 AND \"Employees\" BETWEEN 1 AND 100 AND (\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\") AND \"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')"));
    }
    public void Configure(EntityTypeBuilder<AppointmentStage> b)
    {
        b.ConfigureEntity("AppointmentStages"); b.HasAlternateKey(x=>new{x.BusinessId,x.AppointmentId,x.Id});
        b.Property(x=>x.Name).HasMaxLength(100).IsRequired();
        b.HasOne<WorkArea>().WithMany().HasForeignKey(x=>new{x.BusinessId,x.WorkAreaId}).HasPrincipalKey(x=>new{x.BusinessId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.BusinessId,x.AppointmentId,x.Sequence}).IsUnique();
        b.ToTable("AppointmentStages",t=>t.HasCheckConstraint("CK_AppointmentStages_Plan","\"Sequence\" BETWEEN 1 AND 20 AND \"OffsetMinutes\" >= 0 AND length(btrim(\"Name\")) BETWEEN 1 AND 100 AND \"Minutes\" BETWEEN 1 AND 10080 AND \"Employees\" BETWEEN 1 AND 100"));
    }
}
