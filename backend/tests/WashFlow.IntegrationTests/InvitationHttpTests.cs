using System.Net;
using System.Net.Http.Json;
namespace WashFlow.IntegrationTests;
public sealed class InvitationHttpTests
{
    [Fact]
    public async Task InvitationOperationsRequireAuthenticationAndRegistrationValidatesInput()
    {
        using var api = new ApiFactory(); using var client = api.CreateClient();
        var path = $"/api/v1/businesses/{Guid.NewGuid()}/employee-invitations";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        foreach (var endpoint in new[] { path, path + $"/{Guid.NewGuid()}/resend", path + $"/{Guid.NewGuid()}/revoke", "/api/v1/employee-invitations/accept" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(endpoint, new { token = "bad", email = "a@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/employee-invitations/register", new { })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/v1/employee-invitations/register", new { token = "invalid", fullName = "Test", email = "a@example.test", password = "password" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
