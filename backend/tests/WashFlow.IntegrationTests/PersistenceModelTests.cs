using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Persistence;

namespace WashFlow.IntegrationTests;

[Trait("Category", "PostgreSQL")]
public sealed class PersistenceModelTests(PostgresModelFixture fixture) : IClassFixture<PostgresModelFixture>
{
    private static TokenHash Hash() => new(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
    private static User NewUser(string? email = null) => new("Test User", email ?? $"{Guid.NewGuid():N}@example.test", "encoded-test-password-hash");
    private WashFlowDbContext NewContext() => new(fixture.Options);

    [PostgresFact]
    public async Task MigrationAppliesAndCanBeAppliedAgainWithoutDrift()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        Assert.Equal(7, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        // Queries prove all twenty-three tables actually exist after migration.
        await context.Users.CountAsync();
        await context.Businesses.CountAsync();
        await context.BusinessMemberships.CountAsync();
        await context.BusinessMembershipRoles.CountAsync();
        await context.CustomerProfiles.CountAsync();
        await context.EmailVerificationTokens.CountAsync();
        await context.PasswordResetTokens.CountAsync();
        await context.EmployeeInvitations.CountAsync();
        await context.DeviceSessions.CountAsync();
        await context.RefreshTokens.CountAsync();
        await context.WeeklyOpeningDays.CountAsync();
        await context.LocalCustomers.CountAsync();
        await context.LocalVehicles.CountAsync();
        await context.OwnedVehicles.CountAsync();
        await context.BusinessServices.CountAsync();
        await context.ServiceOfferings.CountAsync();
        await context.WorkAreas.CountAsync();
        await context.ServiceSchedulingRequirements.CountAsync();
        await context.EmployeeAvailabilities.CountAsync();
        await context.Appointments.CountAsync();
        await context.AppointmentEvents.CountAsync();
        await context.AppointmentResourceReservations.CountAsync();
        await context.SchedulingCreateReceipts.CountAsync();
    }

    [PostgresFact]
    public async Task NormalizedEmailIsUniqueAcrossAccounts()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var email = $"{Guid.NewGuid():N}@example.test";
        context.Users.Add(NewUser(email));
        await context.SaveChangesAsync();
        context.Users.Add(NewUser(" " + email.ToUpperInvariant() + " "));
        await AssertConstraint(context, PostgresErrorCodes.UniqueViolation);
    }

    [PostgresFact]
    public async Task CustomerCanHaveNoMembershipOrOwnTwoBusinessesWithBothRoles()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        context.Add(user);
        context.Add(new CustomerProfile { UserId = user.Id });
        await context.SaveChangesAsync();
        Assert.Empty(await context.BusinessMemberships.Where(x => x.UserId == user.Id).ToListAsync());
        foreach (var name in new[] { "Branch A", "Branch B" })
        {
            var business = new Business { Name = name };
            var membership = new BusinessMembership { UserId = user.Id, BusinessId = business.Id };
            context.AddRange(business, membership);
            context.AddRange(new BusinessMembershipRole { BusinessMembershipId = membership.Id, Role = MembershipRole.Owner },
                new BusinessMembershipRole { BusinessMembershipId = membership.Id, Role = MembershipRole.Employee });
        }
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.BusinessMemberships.CountAsync(x => x.UserId == user.Id));
        Assert.Equal(4, await context.BusinessMembershipRoles.Join(context.BusinessMemberships.Where(x => x.UserId == user.Id),
            role => role.BusinessMembershipId, membership => membership.Id, (role, _) => role).CountAsync());
    }

    [PostgresFact]
    public async Task DuplicateMembershipIsRejected()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        var business = new Business { Name = "Test" };
        context.AddRange(user, business, new BusinessMembership { UserId = user.Id, BusinessId = business.Id });
        await context.SaveChangesAsync();
        context.Add(new BusinessMembership { UserId = user.Id, BusinessId = business.Id });
        await AssertConstraint(context, PostgresErrorCodes.UniqueViolation);
    }

    [PostgresFact]
    public async Task UnsupportedRoleIsRejectedByDatabase()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var membership = await AddMembership(context);
        context.Add(new BusinessMembershipRole { BusinessMembershipId = membership.Id, Role = (MembershipRole)99 });
        await AssertConstraint(context, PostgresErrorCodes.CheckViolation);
    }

    [PostgresFact]
    public async Task InvitationCannotReferenceAnotherTenantsIssuer()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var membership = await AddMembership(context);
        var other = new Business { Name = "Other tenant" };
        context.Add(other);
        context.Add(new EmployeeInvitation
        {
            BusinessId = other.Id, InvitedByMembershipId = membership.Id,
            NormalizedEmail = "EMPLOYEE@EXAMPLE.TEST", TokenHash = Hash(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await AssertConstraint(context, PostgresErrorCodes.ForeignKeyViolation);
    }

    [PostgresFact]
    public async Task UserDeletionIsRestrictedWhileCustomerProfileExists()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        context.AddRange(user, new CustomerProfile { UserId = user.Id });
        await context.SaveChangesAsync();
        // A direct delete exercises PostgreSQL RESTRICT rather than EF's tracked relationship fixup.
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Users\" WHERE \"Id\" = {user.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [PostgresFact]
    public async Task DuplicateCustomerProfileIsRejected()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        context.AddRange(user, new CustomerProfile { UserId = user.Id });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.Add(new CustomerProfile { UserId = user.Id });
        await AssertConstraint(context, PostgresErrorCodes.UniqueViolation);
    }

    [PostgresFact]
    public async Task SessionsCannotExceedThirtyDays()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        context.Add(user);
        context.Add(new DeviceSession { UserId = user.Id, SecurityStamp = user.SecurityStamp,
            DeviceName = "Test", ExpiresAt = DateTimeOffset.UtcNow.AddDays(31) });
        await AssertConstraint(context, PostgresErrorCodes.CheckViolation);
    }

    [PostgresFact]
    public async Task ExpiredVerificationRecordIsRejected()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        context.Add(user);
        context.Add(new EmailVerificationToken { UserId = user.Id, SecurityStamp = user.SecurityStamp,
            NormalizedEmail = user.NormalizedEmail, TokenHash = Hash(), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await AssertConstraint(context, PostgresErrorCodes.CheckViolation);
    }

    [PostgresFact]
    public async Task OnlyOneUnconsumedRefreshTokenCanExistPerSession()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var session = await AddSession(context);
        context.Add(new RefreshToken { DeviceSessionId = session.Id, TokenHash = Hash() });
        await context.SaveChangesAsync();
        context.Add(new RefreshToken { DeviceSessionId = session.Id, TokenHash = Hash() });
        await AssertConstraint(context, PostgresErrorCodes.UniqueViolation);
    }

    [PostgresFact]
    public async Task RefreshRotationPreservesHistoryAndRejectsCrossSessionReplacement()
    {
        await using var context = NewContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var firstSession = await AddSession(context);
        var secondSession = await AddSession(context);
        var oldToken = new RefreshToken { DeviceSessionId = firstSession.Id, TokenHash = Hash() };
        context.Add(oldToken);
        await context.SaveChangesAsync();
        oldToken.ConsumedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        var nextToken = new RefreshToken { DeviceSessionId = firstSession.Id, TokenHash = Hash() };
        var foreignToken = new RefreshToken { DeviceSessionId = secondSession.Id, TokenHash = Hash() };
        context.AddRange(nextToken, foreignToken);
        await context.SaveChangesAsync();
        oldToken.ReplacedByTokenId = nextToken.Id;
        await context.SaveChangesAsync();
        oldToken.ReplacedByTokenId = foreignToken.Id;
        await AssertConstraint(context, PostgresErrorCodes.ForeignKeyViolation);
    }

    [PostgresFact]
    public async Task ConcurrentTokenConsumptionCannotSilentlyOverwrite()
    {
        // Separate contexts need committed data; the fixture's isolated schema is removed on disposal.
        Guid id;
        await using (var seed = NewContext())
        {
            var session = await AddSession(seed);
            var token = new RefreshToken { DeviceSessionId = session.Id, TokenHash = Hash() };
            seed.Add(token);
            await seed.SaveChangesAsync();
            id = token.Id;
        }
        await using var first = NewContext();
        await using var second = NewContext();
        var firstToken = await first.RefreshTokens.SingleAsync(x => x.Id == id);
        var secondToken = await second.RefreshTokens.SingleAsync(x => x.Id == id);
        firstToken.ConsumedAt = DateTimeOffset.UtcNow;
        await first.SaveChangesAsync();
        secondToken.ConsumedAt = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static async Task<BusinessMembership> AddMembership(WashFlowDbContext context)
    {
        var user = NewUser();
        var business = new Business { Name = "Test" };
        var membership = new BusinessMembership { UserId = user.Id, BusinessId = business.Id };
        context.AddRange(user, business, membership);
        await context.SaveChangesAsync();
        return membership;
    }

    private static async Task<DeviceSession> AddSession(WashFlowDbContext context)
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;
        var session = new DeviceSession { UserId = user.Id, SecurityStamp = user.SecurityStamp,
            DeviceName = "Test device", CreatedAt = now, ExpiresAt = now.AddDays(30) };
        context.AddRange(user, session);
        await context.SaveChangesAsync();
        return session;
    }

    private static async Task AssertConstraint(WashFlowDbContext context, string sqlState)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(sqlState, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }
}
