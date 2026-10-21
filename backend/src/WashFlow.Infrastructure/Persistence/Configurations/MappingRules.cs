using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashFlow.Domain.Common;
using WashFlow.Domain.Identity;

namespace WashFlow.Infrastructure.Persistence.Configurations;

internal static class MappingRules
{
    public static void ConfigureEntity<T>(this EntityTypeBuilder<T> builder, string table) where T : class, IEntity
    {
        builder.ToTable(table, t =>
        {
            t.HasCheckConstraint($"CK_{table}_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            t.HasCheckConstraint($"CK_{table}_Timestamps", "\"UpdatedAt\" >= \"CreatedAt\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
    }

    public static void ConfigureHash<T>(this EntityTypeBuilder<T> builder, string table) where T : class
    {
        builder.Property<TokenHash>("TokenHash").HasConversion(x => x.Value, x => new TokenHash(x))
            .HasMaxLength(64).IsRequired();
        builder.HasIndex("TokenHash").IsUnique();
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_TokenHash", "\"TokenHash\" ~ '^[0-9a-f]{64}$'"));
    }

    public static void ConfigureExpiring<T>(this EntityTypeBuilder<T> builder, string table, string? consumed = "ConsumedAt") where T : class
    {
        builder.ToTable(table, t =>
        {
            t.HasCheckConstraint($"CK_{table}_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
            t.HasCheckConstraint($"CK_{table}_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
            if (consumed is not null)
                t.HasCheckConstraint($"CK_{table}_Consumption", $"\"{consumed}\" IS NULL OR (\"{consumed}\" >= \"CreatedAt\" AND \"{consumed}\" < \"ExpiresAt\")");
        });
        builder.HasIndex("ExpiresAt");
    }

    public static void NonBlank<T>(this EntityTypeBuilder<T> builder, string table, string property, int maxLength) where T : class
    {
        builder.Property<string>(property).HasMaxLength(maxLength).IsRequired();
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_{property}", $"length(btrim(\"{property}\")) > 0"));
    }
}
