using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;
using WashFlow.Infrastructure.Authentication;

namespace WashFlow.IntegrationTests;

public sealed class EmailVerificationHttpTests
{
    [Fact]
    public async Task InvalidVerificationIsGenericTurkishAndDoesNotReturnSubmittedToken()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        const string raw = "INVALID_SECRET_MUST_NOT_BE_ECHOED";
        using var response = await client.PostAsJsonAsync("/api/v1/auth/email/verify", new { token = raw });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("invalidVerificationToken", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("İstek geçersiz.", json.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain(raw, body);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ResendReturnsIdenticalAcknowledgementsAndIsIpRateLimited()
    {
        using var root = new ApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailVerificationStore>(); services.AddSingleton<IEmailVerificationStore, EmptyStore>();
        }));
        using var client = factory.CreateClient();
        string? acknowledgement = null;
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/email/resend", new { email = $"different{i}@example.test" });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            acknowledgement ??= body;
            Assert.Equal(acknowledgement, body);
        }
        using var limited = await client.PostAsJsonAsync("/api/v1/auth/email/resend", new { email = "another@example.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ResendValidatesEmailWithoutCheckingAccountExistence()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/email/resend", new { email = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation", json.RootElement.GetProperty("code").GetString());
        Assert.Contains("e-posta", json.RootElement.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    private sealed class EmptyStore : IEmailVerificationStore
    {
        public Task<VerificationRecipient?> ReplaceAsync(string email, TokenHash hash, DateTimeOffset now, VerificationOptions options, CancellationToken ct) => Task.FromResult<VerificationRecipient?>(null);
        public Task<bool> VerifyAsync(TokenHash hash, DateTimeOffset now, CancellationToken ct) => Task.FromResult(false);
    }
}
