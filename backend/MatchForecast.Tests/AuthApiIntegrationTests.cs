using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Request;
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

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string password = TestAuth.Password) =>
        client.PostAsJsonAsync("/api/auth/token", new GetTokenRequest(TestAuth.UserName, password));

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshToken));

    [Fact]
    public async Task GetToken_ReturnsTokens_WithValidCredentials()
    {
        var factory = _factory.WithTestJwt();
        await factory.SeedUserAsync();
        var client = factory.CreateClient();

        var response = await LoginAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(token.RefreshToken));
        Assert.Equal("Bearer", token.TokenType);
        Assert.True(token.ExpiresAt > DateTimeOffset.Now);
        Assert.True(token.RefreshTokenExpiresAt > token.ExpiresAt);
    }

    [Fact]
    public async Task GetToken_ReturnsUnauthorized_WithWrongPassword()
    {
        var factory = _factory.WithTestJwt();
        await factory.SeedUserAsync();

        var response = await LoginAsync(factory.CreateClient(), "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_ReturnsUnauthorized_ForInactiveUser()
    {
        var factory = _factory.WithTestJwt();
        await factory.SeedUserAsync(isActive: false);

        var response = await LoginAsync(factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndReuseRevokesAllSessions()
    {
        var factory = _factory.WithTestJwt();
        await factory.SeedUserAsync();
        var client = factory.CreateClient();
        var first = (await (await LoginAsync(client)).Content.ReadFromJsonAsync<TokenResponse>())!;

        // İlk yenileme: yeni çift döner, refresh token değişir.
        var refreshed = await RefreshAsync(client, first.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        // Eski token tekrar kullanılırsa reddedilir ve zincirdeki yeni token da iptal edilir (reuse detection).
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Revoke_PreventsFurtherRefresh()
    {
        var factory = _factory.WithTestJwt();
        await factory.SeedUserAsync();
        var client = factory.CreateClient();
        var token = (await (await LoginAsync(client)).Content.ReadFromJsonAsync<TokenResponse>())!;

        var revoke = await client.PostAsJsonAsync("/api/auth/revoke", new RefreshTokenRequest(token.RefreshToken));

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, token.RefreshToken)).StatusCode);
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
        // Uçtan uca: kullanıcı adı/şifre ile alınan token ile korunan endpoint'e erişim.
        var mockOdds = new Mock<IOddsProvider>();
        mockOdds.Setup(o => o.GetMatchesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var factory = _factory.WithTestJwt().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(mockOdds.Object)));
        await factory.SeedUserAsync();
        var client = factory.CreateClient();

        var token = (await (await LoginAsync(client)).Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/api/matches?date=2026-09-25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UsersEndpoint_ReturnsForbidden_WithoutAdminRole()
    {
        var client = _factory.CreateAuthorizedClient();

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UsersEndpoint_ReturnsOk_WithAdminRole()
    {
        // Token'daki "role" claim'inin [Authorize(Roles = ...)] tarafından okunduğunu doğrular.
        var client = _factory.CreateAuthorizedClient(RoleNames.Admin);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}