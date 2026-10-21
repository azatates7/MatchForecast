using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MatchForecast.Tests;

public class HealthCheckIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Liveness_ReturnsOk_WithoutToken()
    {
        var client = factory.WithTestJwt().CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}