using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WashFlow.IntegrationTests;

public sealed class SchedulingHttpTests
{
    [Fact]
    public async Task EverySchedulingEndpointAndOpenApiRequireAuthenticationBeforeDatabaseAccess()
    {
        using var api = new ApiFactory(); using var client = api.CreateClient(); var b = Guid.NewGuid(); var id = Guid.NewGuid();
        var root = $"/api/v1/businesses/{b}";
        var routes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/work-areas"), (HttpMethod.Post, "/work-areas"), (HttpMethod.Put, $"/work-areas/{id}"), (HttpMethod.Post, $"/work-areas/{id}/archive"),
            (HttpMethod.Get, $"/services/{id}/scheduling-requirements"), (HttpMethod.Put, $"/services/{id}/offerings/Car/scheduling-requirement"), (HttpMethod.Post, $"/services/{id}/offerings/Car/scheduling-requirement/disable"),
            (HttpMethod.Get, "/scheduling-employees"), (HttpMethod.Get, $"/employees/{id}/availability"), (HttpMethod.Post, $"/employees/{id}/availability"), (HttpMethod.Put, $"/employees/{id}/availability/{id}"), (HttpMethod.Post, $"/employees/{id}/availability/{id}/archive"),
            (HttpMethod.Post, "/appointments"), (HttpMethod.Get, "/appointments"), (HttpMethod.Get, $"/appointments/{id}"), (HttpMethod.Post, "/appointments/availability-preview"),
            (HttpMethod.Post, $"/appointments/{id}/reschedule"), (HttpMethod.Post, $"/appointments/{id}/cancel"), (HttpMethod.Get, $"/appointments/{id}/events")
        };
        foreach (var (method, path) in routes)
        {
            using var request = new HttpRequestMessage(method, root + path);
            if (method != HttpMethod.Get) request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal("unauthenticated", body.RootElement.GetProperty("code").GetString());
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var invalid = await client.GetAsync(root + "/work-areas"); Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var documentation = await client.GetAsync("/openapi/v1.json"); Assert.Equal(HttpStatusCode.Unauthorized, documentation.StatusCode);
    }
}
