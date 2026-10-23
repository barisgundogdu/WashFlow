using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WashFlow.Infrastructure.Persistence;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Authentication;

namespace WashFlow.UnitTests;

public sealed class CoreAuthenticationTests
{
    private static readonly PasswordService Passwords = new();
    private static JwtSettings Settings() => new() { Issuer = "test", Audience = "test-client",
        SigningKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32) };

    [Fact]
    public void IdentityPasswordsAreSaltedAndPreserveWhitespace()
    {
        const string password = " correct horse battery staple ";
        var first = Passwords.Hash(password);
        Assert.NotEqual(first, Passwords.Hash(password));
        Assert.DoesNotContain(password, first);
        Assert.True(Passwords.Verify(first, password, out _));
        Assert.False(Passwords.Verify(first, password.Trim(), out _));
        Assert.False(Passwords.Verify(null, password, out _));
    }

    [Fact]
    public void RandomRefreshCredentialsAreUniqueAndOnlyTheirDigestsAreStored()
    {
        var generator = new RefreshTokenGenerator();
        var tokens = Enumerable.Range(0, 100).Select(_ => generator.Generate()).ToArray();
        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, token => { Assert.Equal(43, token.Length); Assert.Equal(64, generator.Hash(token).Value.Length); });
        Assert.Equal(generator.Hash(tokens[0]), generator.Hash(tokens[0]));
    }

    [Fact]
    public void JwtIsFifteenMinutesAndHasNoTenantOrRoleClaims()
    {
        var settings = Settings();
        var now = DateTimeOffset.UtcNow;
        var session = new DeviceSession { UserId = Guid.NewGuid(), SecurityStamp = Guid.NewGuid(),
            DeviceName = "Test", ExpiresAt = now.AddDays(30) };
        var result = new AccessTokenIssuer(settings, TimeProvider.System).Issue(session);
        var principal = new JwtSecurityTokenHandler().ValidateToken(result.Token, settings.ValidationParameters(), out var token);
        Assert.Equal(session.UserId.ToString(), principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
        Assert.DoesNotContain(principal.Claims, c => c.Type.Contains("role", StringComparison.OrdinalIgnoreCase) || c.Type == "tenantId");
        var invalid = Settings().ValidationParameters();
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(result.Token, invalid, out _));
        invalid = settings.ValidationParameters(); invalid.ValidIssuer = "other";
        Assert.Throws<SecurityTokenInvalidIssuerException>(() => new JwtSecurityTokenHandler().ValidateToken(result.Token, invalid, out _));
        invalid = settings.ValidationParameters(); invalid.ValidAudience = "other";
        Assert.Throws<SecurityTokenInvalidAudienceException>(() => new JwtSecurityTokenHandler().ValidateToken(result.Token, invalid, out _));
    }

    [Fact]
    public void ExpiredJwtIsRejected()
    {
        var settings = Settings();
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var session = new DeviceSession { UserId = Guid.NewGuid(), SecurityStamp = Guid.NewGuid(),
            DeviceName = "Test", ExpiresAt = past.AddDays(30) };
        var token = new AccessTokenIssuer(settings, new FixedClock(past)).Issue(session).Token;
        Assert.Throws<SecurityTokenExpiredException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, settings.ValidationParameters(), out _));
    }

    [Fact]
    public void JwtConfigurationFailsClosedWithoutEchoingASecret()
    {
        var configuration = new ConfigurationManager();
        configuration["Jwt:SigningKey"] = "sensitive-not-valid-base64";
        var error = Assert.Throws<InvalidOperationException>(() => JwtSettings.Read(configuration));
        Assert.DoesNotContain(configuration["Jwt:SigningKey"]!, error.Message);
        configuration["Jwt:SigningKey"] = Convert.ToBase64String(new byte[8]);
        Assert.Throws<InvalidOperationException>(() => JwtSettings.Read(configuration));
    }

    [Fact]
    public void RefreshDigestLookupAndSessionLockTranslateWithoutAConnection()
    {
        using var db = new WashFlowDbContextFactory().CreateDbContext([]);
        var hash = new RefreshTokenGenerator().Hash("unit-test-token");
        Assert.Contains("WHERE", db.RefreshTokens.Where(x => x.TokenHash == hash).ToQueryString());
        var id = Guid.NewGuid();
        Assert.Contains("FOR UPDATE", db.DeviceSessions.FromSqlInterpolated(
            $"SELECT *, xmin FROM \"DeviceSessions\" WHERE \"Id\" = {id} FOR UPDATE").ToQueryString());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void JwtNeverOutlivesAbsoluteSessionExpiry()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2);
        var session = new DeviceSession { UserId = Guid.NewGuid(), SecurityStamp = Guid.NewGuid(), DeviceName = "Test", ExpiresAt = expiry };
        var result = new AccessTokenIssuer(Settings(), TimeProvider.System).Issue(session);
        Assert.True(result.ExpiresAt <= expiry);
    }

    [Fact]
    public async Task RegistrationHashesPasswordAndNeverMarksEmailVerified()
    {
        var store = new StubStore();
        var service = Service(store);
        var result = await service.RegisterAsync(new() { OwnerFullName = "Ayşe", BusinessName = "Şube",
            Email = "ayse@example.test", Password = " test password " }, true, default);
        Assert.True(result.Succeeded);
        Assert.NotNull(store.Registered);
        Assert.Null(store.Registered.EmailVerifiedAt);
        Assert.True(Passwords.Verify(store.Registered.PasswordHash, " test password ", out _));
        Assert.Equal("Şube", store.BusinessName);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task UnknownWrongAndUnverifiedLoginsHaveTheSameFailure(bool exists, bool verified, bool wrongPassword)
    {
        var store = new StubStore();
        if (exists)
        {
            store.Found = new User("Test", "a@example.test", Passwords.Hash("correct password"));
            if (verified) store.Found.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }
        var result = await Service(store).LoginAsync(new() { Email = "a@example.test",
            Password = wrongPassword ? "wrong password" : "correct password" }, default);
        Assert.Null(result.Value);
        Assert.Null(result.Errors);
        Assert.Equal(0, store.SessionsStarted);
    }

    [Fact]
    public async Task InvalidInputsNeverReachPersistence()
    {
        var store = new StubStore();
        var result = await Service(store).RegisterAsync(new() { Email = "bad", Password = "" }, false, default);
        Assert.NotNull(result.Errors);
        Assert.Contains("email", result.Errors.Keys);
        Assert.Contains("fullName", result.Errors.Keys);
        Assert.Null(store.Registered);
        Assert.Null((await Service(store).RefreshAsync(new() { RefreshToken = "not-a-token" }, default)).Value);
    }

    private static AuthService Service(StubStore store) => new(store, Passwords, new RefreshTokenGenerator(),
        new AccessTokenIssuer(Settings(), TimeProvider.System), TimeProvider.System, EmailVerificationTests.CreateService());

    private sealed class StubStore : IAuthStore
    {
        public User? Found;
        public User? Registered;
        public string? BusinessName;
        public int SessionsStarted;
        public Task<bool> RegisterAsync(User user, string? businessName, EmailVerificationToken verification, CancellationToken ct)
        { Registered = user; BusinessName = businessName; return Task.FromResult(true); }
        public Task<User?> FindUserAsync(string email, CancellationToken ct) => Task.FromResult(Found);
        public Task<DeviceSession?> StartSessionAsync(User user, string? hash, string deviceName, TokenHash tokenHash, DateTimeOffset now, CancellationToken ct)
        { SessionsStarted++; return Task.FromResult<DeviceSession?>(null); }
        public Task<DeviceSession?> RotateAsync(TokenHash oldHash, TokenHash newHash, DateTimeOffset now, CancellationToken ct) => Task.FromResult<DeviceSession?>(null);
        public Task LogoutAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct) => Task.FromResult(false);
    }
}
