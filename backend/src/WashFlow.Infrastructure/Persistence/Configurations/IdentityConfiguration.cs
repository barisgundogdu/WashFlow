using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Identity;

namespace WashFlow.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ConfigureEntity("Users");
        builder.NonBlank("Users", nameof(User.FullName), 200);
        builder.NonBlank("Users", nameof(User.Email), 320);
        builder.NonBlank("Users", nameof(User.NormalizedEmail), 320);
        builder.NonBlank("Users", nameof(User.PasswordHash), 2048);
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.ToTable("Users", t =>
        {
            t.HasCheckConstraint("CK_Users_Verification", "\"EmailVerifiedAt\" IS NULL OR \"EmailVerifiedAt\" >= \"CreatedAt\"");
            t.HasCheckConstraint("CK_Users_SecurityStamp", "\"SecurityStamp\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
    }
}

public sealed class EmailVerificationTokenConfiguration : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ConfigureEntity("EmailVerificationTokens");
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"ConsumedAt\" IS NULL AND \"RevokedAt\" IS NULL");
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.ConfigureHash("EmailVerificationTokens");
        builder.ConfigureExpiring("EmailVerificationTokens");
        builder.NonBlank("EmailVerificationTokens", nameof(EmailVerificationToken.NormalizedEmail), 320);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ConfigureEntity("PasswordResetTokens");
        builder.ConfigureHash("PasswordResetTokens");
        builder.ConfigureExpiring("PasswordResetTokens");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(EntityTypeBuilder<DeviceSession> builder)
    {
        builder.ConfigureEntity("DeviceSessions");
        builder.ConfigureExpiring("DeviceSessions", consumed: null);
        builder.NonBlank("DeviceSessions", nameof(DeviceSession.DeviceName), 200);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("DeviceSessions", t => t.HasCheckConstraint("CK_DeviceSessions_MaxLifetime",
            "\"ExpiresAt\" <= \"CreatedAt\" + interval '720 hours'"));
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ConfigureEntity("RefreshTokens");
        builder.ConfigureHash("RefreshTokens");
        builder.HasAlternateKey(x => new { x.DeviceSessionId, x.Id });
        builder.HasOne<DeviceSession>().WithMany().HasForeignKey(x => x.DeviceSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RefreshToken>().WithMany()
            .HasForeignKey(x => new { x.DeviceSessionId, x.ReplacedByTokenId })
            .HasPrincipalKey(x => new { x.DeviceSessionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ReplacedByTokenId).IsUnique().HasFilter("\"ReplacedByTokenId\" IS NOT NULL");
        builder.HasIndex(x => x.DeviceSessionId).IsUnique()
            .HasFilter("\"ConsumedAt\" IS NULL AND \"RevokedAt\" IS NULL");
        builder.ToTable("RefreshTokens", t =>
        {
            t.HasCheckConstraint("CK_RefreshTokens_Consumption", "\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\"");
            t.HasCheckConstraint("CK_RefreshTokens_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
            t.HasCheckConstraint("CK_RefreshTokens_Replacement", "\"ReplacedByTokenId\" IS NULL OR (\"ReplacedByTokenId\" <> \"Id\" AND \"ConsumedAt\" IS NOT NULL)");
        });
    }
}
