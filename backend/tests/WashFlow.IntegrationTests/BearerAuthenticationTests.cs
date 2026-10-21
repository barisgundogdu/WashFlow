using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Authentication;

namespace WashFlow.IntegrationTests;

public sealed class BearerAuthenticationTests
{
    [Fact]
    public async Task BearerMiddlewareValidatesIssuerAudienceSignatureExpiryAndSessionRevocation()
    {
        var settings = new JwtSettings { Issuer = "middleware-test", Audience = "mobile-test",
            SigningKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32) };
        var store = new SessionStore();
        using var root = new ApiFactory();
        using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", settings.Issuer);
            builder.UseSetting("Jwt:Audience", settings.Audience);
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(settings.SigningKey));
            builder.ConfigureServices(services => { services.RemoveAll<IAuthStore>(); services.AddSingleton<IAuthStore>(store); });
        });
        using var client = factory.CreateClient();
        var session = new DeviceSession { UserId = Guid.NewGuid(), SecurityStamp = Guid.NewGuid(),
            DeviceName = "Test", ExpiresAt = DateTimeOffset.UtcNow.AddDays(30) };
        foreach (var invalid in new[]
        {
            new JwtSettings { Issuer = "wrong", Audience = settings.Audience, SigningKey = settings.SigningKey },
            new JwtSettings { Issuer = settings.Issuer, Audience = "wrong", SigningKey = settings.SigningKey },
            new JwtSettings { Issuer = settings.Issuer, Audience = settings.Audience, SigningKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32) }
        })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                new AccessTokenIssuer(invalid, TimeProvider.System).Issue(session).Token);
            using var rejected = await client.PostAsync("/api/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new AccessTokenIssuer(settings, new PastClock()).Issue(session).Token);
        using var expired = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(0, store.Logouts);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new AccessTokenIssuer(settings, TimeProvider.System).Issue(session).Token);
        using var success = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Equal(session.UserId, store.UserId); Assert.Equal(session.Id, store.SessionId);
        using var revoked = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(1, store.Logouts);
    }

    private sealed class PastClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(-1); }
    private sealed class SessionStore : IAuthStore
    {
        public int Logouts; public Guid UserId; public Guid SessionId;
        public Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct) => Task.FromResult(Logouts == 0);
        public Task LogoutAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct)
        { UserId = userId; SessionId = sessionId; Logouts++; return Task.CompletedTask; }
        public Task RegisterAsync(User user, string? name, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindUserAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeviceSession?> StartSessionAsync(User user, string? hash, string name, TokenHash tokenHash, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeviceSession?> RotateAsync(TokenHash oldHash, TokenHash newHash, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
