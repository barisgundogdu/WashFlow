using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Catalog;
namespace WashFlow.Infrastructure.Persistence.Configurations;

public sealed class CatalogConfiguration : IEntityTypeConfiguration<BusinessService>, IEntityTypeConfiguration<ServiceOffering>
{
    public void Configure(EntityTypeBuilder<BusinessService> b)
    {
        b.ConfigureEntity("BusinessServices");
        b.Ignore(x => x.Available);
        b.NonBlank("BusinessServices", nameof(BusinessService.Name), 200);
        b.NonBlank("BusinessServices", nameof(BusinessService.SearchName), 200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BusinessId, x.SearchName, x.Id });
        b.ToTable("BusinessServices", t => {
            t.HasCheckConstraint("CK_BusinessServices_Publication", "NOT \"Published\" OR \"Enabled\"");
            t.HasCheckConstraint("CK_BusinessServices_Archive", "\"ArchivedAt\" IS NULL OR (\"ArchivedAt\" >= \"CreatedAt\" AND NOT \"Enabled\" AND NOT \"Published\")");
        });
    }
    public void Configure(EntityTypeBuilder<ServiceOffering> b)
    {
        b.ToTable("ServiceOfferings", t => {
            t.HasCheckConstraint("CK_ServiceOfferings_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
            t.HasCheckConstraint("CK_ServiceOfferings_Price", "\"Price\" BETWEEN 0 AND 9999999.99 AND scale(\"Price\") <= 2");
            t.HasCheckConstraint("CK_ServiceOfferings_Currency", "\"Currency\" = 'TRY'");
            t.HasCheckConstraint("CK_ServiceOfferings_Estimates", "\"ActiveMinutes\" BETWEEN 1 AND 10080 AND (\"WaitingMinutes\" IS NULL OR \"WaitingMinutes\" BETWEEN 0 AND 43200) AND \"Employees\" BETWEEN 1 AND 100");
        });
        b.HasKey(x => new { x.ServiceId, x.Category });
        b.Property(x => x.Category).HasMaxLength(32);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Price).HasPrecision(9, 2);
        b.HasOne<BusinessService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
