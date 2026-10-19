using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MatchForecast.Api.Services.Security;
using MatchForecast.Models.Request;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

/*
 * Her test WithTestJwt/CreateAuthorizedClient ile yeni bir host (dolayısıyla yeni, boş rate limiter sayaçları) kullanır;
 * testler birbirinin hakkını tüketmez. Limitler appsettings varsayılanlarıdır (token: 10/dk, cache: 2/60 dk).
 */
public class RateLimitingIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetToken_Returns429_AfterLimitExceeded()
    {
        // Hatalı şifre denemeleri de limite sayılır: brute-force'a karşı koruma.
        var client = factory.WithTestJwt().CreateClient();
        var request = new GetTokenRequest("unknown", "wrong-password");

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/token", request)).StatusCode);

        var rejected = await client.PostAsJsonAsync("/api/auth/token", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("Çok fazla istek", problem!.Detail);
    }

    [Fact]
    public async Task ClearCache_AllowsTwoCallsPerHour_ThenReturns429()
    {
        var client = factory.CreateAuthorizedClient();

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/cache")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/cache")).StatusCode);

        var rejected = await client.DeleteAsync("/api/cache");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("en fazla 2 kez", problem!.Detail);
    }

    [Fact]
    public async Task ClearCache_WithoutToken_DoesNotConsumeLimit()
    {
        // UseRateLimiter, UseAuthorization'dan sonra: token'sız istekler 401 alır ve saatlik hakkı tüketmez.
        var testFactory = factory.WithTestJwt();
        var anonymous = testFactory.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/cache")).StatusCode);

        var token = testFactory.Services.GetRequiredService<JwtTokenService>().CreateAccessToken(1, "test", []);
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.DeleteAsync("/api/cache")).StatusCode);
    }
}