using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MatchForecast.Api.Services;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MatchForecast.Tests;

public class AuthApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetToken_ReturnsToken_WithoutCredentials()
    {
        var client = _factory.WithTestJwt().CreateClient();

        var response = await client.GetAsync("/api/auth/token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.Equal("Bearer", token.TokenType);
        Assert.True(token.ExpiresAt > DateTimeOffset.Now);
    }

    [Fact]
    public async Task ProtectedEndpoint_ReturnsUnauthorized_WithoutToken()
    {
        var client = _factory.WithTestJwt().CreateClient();

        var response = await client.GetAsync("/api/matches?date=2026-09-25");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_ReturnsUnauthorized_WithInvalidToken()
    {
        var client = _factory.WithTestJwt().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-token");

        var response = await client.GetAsync("/api/matches?date=2026-09-25");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_ReturnsOk_WithTokenFromGetToken()
    {
        // Uçtan uca: GetToken'dan alınan token ile korunan endpoint'e erişim.
        var mockOdds = new Mock<IOddsProvider>();
        mockOdds.Setup(o => o.GetMatchesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var client = _factory.WithTestJwt().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(mockOdds.Object))).CreateClient();

        var token = await client.GetFromJsonAsync<TokenResponse>("/api/auth/token");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var response = await client.GetAsync("/api/matches?date=2026-09-25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}