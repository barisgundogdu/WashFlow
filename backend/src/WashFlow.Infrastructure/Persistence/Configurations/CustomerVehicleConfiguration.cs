using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
namespace WashFlow.Infrastructure.Persistence.Configurations;

public sealed class LocalCustomerConfiguration : IEntityTypeConfiguration<LocalCustomer>
{
    public void Configure(EntityTypeBuilder<LocalCustomer> b)
    {
        b.ConfigureEntity("LocalCustomers");
        b.HasAlternateKey(x => new { x.BusinessId, x.Id });
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.NonBlank("LocalCustomers", nameof(LocalCustomer.FullName), 200);
        b.NonBlank("LocalCustomers", nameof(LocalCustomer.NormalizedName), 200);
        b.Property(x => x.Phone).HasMaxLength(40); b.Property(x => x.NormalizedPhone).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(320); b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.BusinessId, x.NormalizedName, x.Id });
        b.HasIndex(x => new { x.BusinessId, x.NormalizedPhone });
    }
}

public sealed class LocalVehicleConfiguration : IEntityTypeConfiguration<LocalVehicle>
{
    public void Configure(EntityTypeBuilder<LocalVehicle> b)
    {
        b.ConfigureEntity("LocalVehicles");
        b.HasOne<LocalCustomer>().WithMany().HasForeignKey(x => new { x.BusinessId, x.LocalCustomerId }).HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BusinessId, x.NormalizedPlate, x.Id });
        b.HasIndex(x => new { x.BusinessId, x.LocalCustomerId, x.Id });
        b.NonBlank("LocalVehicles", nameof(LocalVehicle.Plate), 32);
        b.NonBlank("LocalVehicles", nameof(LocalVehicle.NormalizedPlate), 64);
        b.NonBlank("LocalVehicles", nameof(LocalVehicle.Brand), 80);
        b.NonBlank("LocalVehicles", nameof(LocalVehicle.Model), 100);
        b.NonBlank("LocalVehicles", nameof(LocalVehicle.Category), 32);
        b.Property(x => x.Color).HasMaxLength(60); b.Property(x => x.Notes).HasMaxLength(2000);
        b.ToTable("LocalVehicles", t =>
        {
            t.HasCheckConstraint("CK_LocalVehicles_Year", "\"ModelYear\" IS NULL OR \"ModelYear\" BETWEEN 1886 AND 2100");
            t.HasCheckConstraint("CK_LocalVehicles_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
            t.HasCheckConstraint("CK_LocalVehicles_Archive", "\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\"");
        });
    }
}

public sealed class OwnedVehicleConfiguration : IEntityTypeConfiguration<OwnedVehicle>
{
    public void Configure(EntityTypeBuilder<OwnedVehicle> b)
    {
        b.ConfigureEntity("OwnedVehicles");
        b.HasOne<CustomerProfile>().WithMany().HasForeignKey(x => x.CustomerProfileId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CustomerProfileId, x.NormalizedPlate, x.Id });
        b.NonBlank("OwnedVehicles", nameof(OwnedVehicle.Plate), 32);
        b.NonBlank("OwnedVehicles", nameof(OwnedVehicle.NormalizedPlate), 64);
        b.NonBlank("OwnedVehicles", nameof(OwnedVehicle.Brand), 80);
        b.NonBlank("OwnedVehicles", nameof(OwnedVehicle.Model), 100);
        b.NonBlank("OwnedVehicles", nameof(OwnedVehicle.Category), 32);
        b.Property(x => x.Color).HasMaxLength(60); b.Property(x => x.Notes).HasMaxLength(2000);
        b.ToTable("OwnedVehicles", t =>
        {
            t.HasCheckConstraint("CK_OwnedVehicles_Year", "\"ModelYear\" IS NULL OR \"ModelYear\" BETWEEN 1886 AND 2100");
            t.HasCheckConstraint("CK_OwnedVehicles_Category", "\"Category\" IN ('Car','Suv','Van','Pickup','Motorcycle','Other')");
            t.HasCheckConstraint("CK_OwnedVehicles_Archive", "\"ArchivedAt\" IS NULL OR \"ArchivedAt\" >= \"CreatedAt\"");
        });
    }
}
