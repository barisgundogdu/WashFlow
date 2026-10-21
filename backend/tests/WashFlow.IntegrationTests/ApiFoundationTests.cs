using System.Net;
using System.Text.Json;

namespace WashFlow.IntegrationTests;

public sealed class ApiFoundationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LivenessDoesNotRequirePostgres()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DevelopmentAlsoHidesExceptionDetails()
    {
        using var development = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("environment", "Development"));
        using var client = development.CreateClient();
        using var response = await client.GetAsync("/__test/failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SENSITIVE_EXCEPTION_DETAIL", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Theory]
    [InlineData("/does-not-exist?secret=DO_NOT_ECHO", 404, "notFound", "Kaynak bulunamadı.")]
    [InlineData("/__test/failure", 500, "unexpected", "İstek işlenemedi.")]
    [InlineData("/health/ready", 503, "unavailable", "Hizmet şu anda kullanılamıyor.")]
    public async Task FailuresReturnSafeTurkishProblemDetails(
        string path, int status, string code, string title)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var problem = json.RootElement;
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("SENSITIVE_EXCEPTION_DETAIL", body);
        Assert.DoesNotContain("DO_NOT_ECHO", body);
        Assert.DoesNotContain("Password", body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(problem.TryGetProperty("detail", out _));
    }
}
