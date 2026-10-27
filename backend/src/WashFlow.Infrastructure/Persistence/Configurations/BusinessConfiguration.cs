using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;

namespace WashFlow.Infrastructure.Persistence.Configurations;

public sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ConfigureEntity("Businesses");
        builder.NonBlank("Businesses", nameof(Business.Name), 200);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.ContactEmail).HasMaxLength(320);
        builder.Property(x => x.AddressLine1).HasMaxLength(250);
        builder.Property(x => x.AddressLine2).HasMaxLength(250);
        builder.Property(x => x.District).HasMaxLength(100);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Region).HasMaxLength(100);
        builder.Property(x => x.PostalCode).HasMaxLength(32);
        builder.Property(x => x.CountryCode).HasMaxLength(2);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.TimeZoneId).HasMaxLength(100);

    }
}

public sealed class BusinessMembershipConfiguration : IEntityTypeConfiguration<BusinessMembership>
{
    public void Configure(EntityTypeBuilder<BusinessMembership> builder)
    {
        builder.ConfigureEntity("BusinessMemberships");
        builder.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique();
        builder.HasAlternateKey(x => new { x.BusinessId, x.Id });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("BusinessMemberships", t => t.HasCheckConstraint("CK_BusinessMemberships_Revocation",
            "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\""));
    }
}

public sealed class BusinessMembershipRoleConfiguration : IEntityTypeConfiguration<BusinessMembershipRole>
{
    public void Configure(EntityTypeBuilder<BusinessMembershipRole> builder)
    {
        builder.ToTable("BusinessMembershipRoles", t => t.HasCheckConstraint("CK_BusinessMembershipRoles_Role", "\"Role\" IN (1, 2)"));
        builder.HasKey(x => new { x.BusinessMembershipId, x.Role });
        builder.Property(x => x.Role).HasConversion<int>();
        builder.HasOne<BusinessMembership>().WithMany().HasForeignKey(x => x.BusinessMembershipId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        builder.ConfigureEntity("CustomerProfiles");
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne<User>().WithOne().HasForeignKey<CustomerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeInvitationConfiguration : IEntityTypeConfiguration<EmployeeInvitation>
{
    public void Configure(EntityTypeBuilder<EmployeeInvitation> builder)
    {
        builder.ConfigureEntity("EmployeeInvitations");
        builder.ConfigureHash("EmployeeInvitations");
        builder.ConfigureExpiring("EmployeeInvitations", "AcceptedAt");
        builder.NonBlank("EmployeeInvitations", nameof(EmployeeInvitation.NormalizedEmail), 320);
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessMembership>().WithMany()
            .HasForeignKey(x => new { x.BusinessId, x.InvitedByMembershipId })
            .HasPrincipalKey(x => new { x.BusinessId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AcceptedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BusinessId, x.NormalizedEmail }).IsUnique()
            .HasFilter("\"AcceptedAt\" IS NULL AND \"RevokedAt\" IS NULL");
        builder.ToTable("EmployeeInvitations", t => t.HasCheckConstraint("CK_EmployeeInvitations_FinalState",
            "\"AcceptedAt\" IS NULL OR \"RevokedAt\" IS NULL"));
        builder.ToTable("EmployeeInvitations", t => t.HasCheckConstraint("CK_EmployeeInvitations_Acceptance",
            "(\"AcceptedAt\" IS NULL) = (\"AcceptedByUserId\" IS NULL)"));
    }
}

public sealed class WeeklyOpeningDayConfiguration : IEntityTypeConfiguration<WeeklyOpeningDay>
{
    public void Configure(EntityTypeBuilder<WeeklyOpeningDay> builder)
    {
        builder.ToTable("WeeklyOpeningDays", t =>
        {
            t.HasCheckConstraint("CK_WeeklyOpeningDays_Day", "\"Day\" BETWEEN 1 AND 7");
            t.HasCheckConstraint("CK_WeeklyOpeningDays_Interval", "(\"Closed\" AND \"OpensAtMinute\" IS NULL AND \"ClosesAtMinute\" IS NULL) OR (NOT \"Closed\" AND \"OpensAtMinute\" IS NOT NULL AND \"ClosesAtMinute\" IS NOT NULL AND \"OpensAtMinute\" >= 0 AND \"OpensAtMinute\" < \"ClosesAtMinute\" AND \"ClosesAtMinute\" <= 1440)");
        });
        builder.HasKey(x => new { x.BusinessId, x.Day });
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}
