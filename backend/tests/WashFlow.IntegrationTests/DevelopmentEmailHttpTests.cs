using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashFlow.Application.Authentication;
using WashFlow.Infrastructure.Email;

namespace WashFlow.IntegrationTests;

public sealed class DevelopmentEmailHttpTests
{
    private const string Path = "/api/dev/email/verification/take";
    private const string Recipient = "local@example.test";

    private static WebApplicationFactory<Program> Configure(ApiFactory root, string environment) =>
        root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (environment == "Development") builder.UseSetting("Email:Provider", "Memory");
        });

    private static async Task<VerificationEmail> Seed(WebApplicationFactory<Program> factory, string token)
    {
        var email = new VerificationEmail
        {
            Recipient = Recipient, Token = token, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        await factory.Services.GetRequiredService<DevelopmentEmailSender>().SendAsync(email, default);
        return email;
    }

    // TestServer has no network socket. Set the actual connection feature, not a
    // client header, to simulate the transport peer seen by the endpoint.
    private static Task<Microsoft.AspNetCore.Http.HttpContext> Take(
        WebApplicationFactory<Program> factory, string? peer, string? header = null,
        string? body = null, string contentType = "application/json") =>
        factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
            context.Request.Method = "POST";
            context.Request.Path = Path;
            context.Request.ContentType = contentType;
            var bytes = System.Text.Encoding.UTF8.GetBytes(body ?? JsonSerializer.Serialize(new { email = Recipient }));
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            if (header is not null) context.Request.Headers[header] = "127.0.0.1";
        });

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task LoopbackTakesLatestMessageOnceWithoutConsumingOtherRecipient(string peer)
    {
        using var root = new ApiFactory();
        using var factory = Configure(root, "Development");
        await Seed(factory, "old-secret");
        var latest = await Seed(factory, "latest-secret");
        var mailbox = factory.Services.GetRequiredService<DevelopmentEmailSender>();
        await mailbox.SendAsync(new VerificationEmail
        {
            Recipient = "other@example.test", Token = "other-secret", ExpiresAt = latest.ExpiresAt
        }, default);

        var response = await Take(factory, peer, body: "{\"email\":\" LOCAL@example.test \"}");
        Assert.Equal(200, response.Response.StatusCode);
        Assert.Equal("no-store", response.Response.Headers.CacheControl);
        Assert.Equal("no-cache", response.Response.Headers.Pragma);
        using var json = await JsonDocument.ParseAsync(response.Response.Body);
        Assert.Equal(latest.Token, json.RootElement.GetProperty("token").GetString());
        Assert.Equal(Recipient, json.RootElement.GetProperty("recipient").GetString());
        Assert.Equal(latest.ExpiresAt, json.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(404, (await Take(factory, peer)).Response.StatusCode);
        Assert.True(mailbox.TryTake("other@example.test", out _));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task NonDevelopmentDoesNotRegisterRouteEvenWithMemorySenderInTestDI(string environment)
    {
        using var root = new ApiFactory();
        using var factory = Configure(root, environment);
        await Seed(factory, "must-remain");
        Assert.DoesNotContain(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints,
            endpoint => endpoint is RouteEndpoint route && route.RoutePattern.RawText == Path);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Path, new { email = Recipient });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("must-remain", await response.Content.ReadAsStringAsync());
        Assert.True(factory.Services.GetRequiredService<DevelopmentEmailSender>().TryTake(Recipient, out _));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("192.0.2.1", null)]
    [InlineData("2001:db8::1", null)]
    [InlineData("::ffff:127.0.0.1", null)]
    [InlineData("192.0.2.1", "X-Forwarded-For")]
    [InlineData("127.0.0.1", "X-Forwarded-For")]
    [InlineData("::1", "Forwarded")]
    [InlineData("127.0.0.1", "X-Forwarded-Host")]
    [InlineData("127.0.0.1", "X-Original-For")]
    [InlineData("127.0.0.1", "Via")]
    public async Task RejectsUnknownNonLoopbackAndProxyRequestsWithoutTakingMessage(string? peer, string? header)
    {
        using var root = new ApiFactory();
        using var factory = Configure(root, "Development");
        await Seed(factory, "must-remain");
        var response = await Take(factory, peer, header);
        Assert.Equal(403, response.Response.StatusCode);
        using var reader = new StreamReader(response.Response.Body);
        Assert.DoesNotContain("must-remain", await reader.ReadToEndAsync());
        Assert.True(factory.Services.GetRequiredService<DevelopmentEmailSender>().TryTake(Recipient, out _));
    }

    [Theory]
    [InlineData("{\"email\":\"invalid\"}", "application/json", 400)]
    [InlineData("null", "application/json", 400)]
    [InlineData("{broken", "application/json", 400)]
    [InlineData("{}", "application/json", 400)]
    [InlineData("{\"email\":\"local@example.test\"}", "text/plain", 415)]
    public async Task InvalidInputDoesNotConsumeMessage(string body, string contentType, int status)
    {
        using var root = new ApiFactory();
        using var factory = Configure(root, "Development");
        await Seed(factory, "must-remain");
        Assert.Equal(status, (await Take(factory, "127.0.0.1", body: body, contentType: contentType)).Response.StatusCode);
        Assert.True(factory.Services.GetRequiredService<DevelopmentEmailSender>().TryTake(Recipient, out _));
    }

    [Fact]
    public async Task DevelopmentWithSmtpDoesNotRegisterMailboxRoute()
    {
        using var root = new ApiFactory();
        using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Email:Provider", "Smtp");
            // ApiFactory normally overrides SMTP to avoid external mail in tests.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DevelopmentEmailSender>();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender, SilentSender>();
            });
        });
        using var client = factory.CreateClient();
        Assert.DoesNotContain(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints,
            endpoint => endpoint is RouteEndpoint route && route.RoutePattern.RawText == Path);
        using var response = await client.PostAsJsonAsync(Path, new { email = Recipient });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class SilentSender : IEmailSender
    {
        public Task SendAsync(VerificationEmail email, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task ConcurrentReadersCannotTakeTheSameMessageTwice()
    {
        using var root = new ApiFactory();
        using var factory = Configure(root, "Development");
        await Seed(factory, "once");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Take(factory, "127.0.0.1")));
        Assert.Single(results, result => result.Response.StatusCode == 200);
        Assert.Equal(7, results.Count(result => result.Response.StatusCode == 404));
    }
}
