using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace WashFlow.IntegrationTests;

public sealed class AuthHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("customers/register")]
    [InlineData("businesses/register")]
    [InlineData("login")]
    public async Task InvalidRequestsReturnTurkishValidationWithoutDatabaseAccess(string endpoint)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/" + endpoint, new { email = "bad", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation", json.RootElement.GetProperty("code").GetString());
        Assert.Contains("e-posta", json.RootElement.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task MalformedJsonIsSafeAndTurkish()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{not-json", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/auth/login", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("İstek", JsonDocument.Parse(text).RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("JsonException", text);
    }

    [Fact]
    public async Task MalformedRefreshAndAnonymousLogoutReturnGenericUnauthorized()
    {
        using var client = factory.CreateClient();
        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "invalid" });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var json = JsonDocument.Parse(await refresh.Content.ReadAsStringAsync());
        Assert.Equal("invalidCredentials", json.RootElement.GetProperty("code").GetString());
        using var logout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
        Assert.Equal("application/problem+json", logout.Content.Headers.ContentType?.MediaType);
    }
}
