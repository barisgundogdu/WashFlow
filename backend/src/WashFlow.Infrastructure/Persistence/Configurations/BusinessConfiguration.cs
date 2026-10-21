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
        builder.HasIndex(x => new { x.BusinessId, x.NormalizedEmail });
        builder.ToTable("EmployeeInvitations", t => t.HasCheckConstraint("CK_EmployeeInvitations_Acceptance",
            "(\"AcceptedAt\" IS NULL) = (\"AcceptedByUserId\" IS NULL)"));
    }
}
