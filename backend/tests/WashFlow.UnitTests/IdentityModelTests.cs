using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.UnitTests;

public sealed class IdentityModelTests
{
    [Fact]
    public void EmailNormalizationIsIndependentOfTurkishCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var user = new User(" Ayşe ", " identity@example.com ", "encoded-adaptive-password-hash");
            Assert.Equal("IDENTITY@EXAMPLE.COM", user.NormalizedEmail);
            Assert.Equal("identity@example.com", user.Email);
            Assert.Equal("Ayşe", user.FullName);
            Assert.Null(user.EmailVerifiedAt);
            Assert.NotEqual(Guid.Empty, user.SecurityStamp);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext-token")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void TokenHashRejectsNonSha256Representations(string value) =>
        Assert.Throws<ArgumentException>(() => new TokenHash(value));

    [Fact]
    public void HashesAreCanonicalAndRedacted()
    {
        var hash = new TokenHash(new string('A', 64));
        Assert.Equal(new string('a', 64), hash.Value);
        Assert.Equal(new TokenHash(new string('a', 64)), hash);
        Assert.Equal("[REDACTED]", hash.ToString());
    }

    [Fact]
    public void RolesDoNotIncludeGlobalOrCustomerGrants() =>
        Assert.Equal(new[] { MembershipRole.Owner, MembershipRole.Employee }, Enum.GetValues<MembershipRole>());

    [Fact]
    public void SensitiveRecordsUseConcurrencyAndNoPlaintextTokenColumns()
    {
        using var context = new WashFlowDbContextFactory().CreateDbContext([]);
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(EmailVerificationToken), typeof(PasswordResetToken), typeof(RefreshToken), typeof(DeviceSession) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
            Assert.DoesNotContain(entity.GetProperties(), p => p.Name is "Token" or "Password" or "RefreshToken");
        }
        var refresh = model.FindEntityType(typeof(RefreshToken))!;
        Assert.Contains(refresh.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(RefreshToken) &&
            key.Properties.Select(p => p.Name).SequenceEqual(new[] { "DeviceSessionId", "ReplacedByTokenId" }));
        Assert.All(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()),
            key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
    }

    [Fact]
    public void InitialMigrationMatchesCurrentModelWithoutAConnection()
    {
        using var context = new WashFlowDbContextFactory().CreateDbContext([]);
        Assert.Single(context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("CK_DeviceSessions_MaxLifetime", sql);
        Assert.Contains("CK_BusinessMembershipRoles_Role", sql);
        Assert.Contains("CK_RefreshTokens_TokenHash", sql);
    }
}
