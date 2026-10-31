using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Security;
using MatchForecast.Api.Services.Security.Guest;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MatchForecast.Tests;

// Misafir kısıtları API tarafında uygulanır: yalnızca bugünün Süper Lig maçları, cache temizleme yok.
public class GuestAccessIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly MatchSummary SuperLig =
        new(1, DateTimeOffset.Now, GuestAccess.League, GuestAccess.Country, "Galatasaray", "Fenerbahçe", "NS");

    private static readonly MatchSummary PremierLeague =
        new(2, DateTimeOffset.Now, "Premier League", "England", "Arsenal", "Chelsea", "NS");

    private HttpClient CreateGuestClient(Mock<IOddsProvider> odds, Mock<IForecastAiClient>? ai = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(odds.Object);
            if (ai is not null)
                services.AddSingleton(ai.Object);
        })).CreateAuthorizedClient(RoleNames.Guest);

    [Fact]
    public async Task GetMatches_Guest_ReturnsOnlyTodaysSuperLigMatches()
    {
        var odds = new Mock<IOddsProvider>();
        odds.Setup(o => o.GetMatchesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([SuperLig, PremierLeague]);

        // Başka bir tarih istense de bugün kullanılır.
        var response = await CreateGuestClient(odds).GetAsync("/api/matches?date=2026-09-25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var matches = await response.Content.ReadFromJsonAsync<List<MatchSummary>>();
        Assert.NotNull(matches);
        Assert.Equal("Galatasaray", Assert.Single(matches).HomeTeam);
        odds.Verify(o => o.GetMatchesAsync(GuestAccess.Today, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearCache_Guest_ReturnsForbidden()
    {
        var response = await CreateGuestClient(new Mock<IOddsProvider>()).DeleteAsync("/api/cache");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetForecast_Guest_ReturnsNotFound_ForNonSuperLigMatch()
    {
        var odds = new Mock<IOddsProvider>();
        odds.Setup(o => o.GetMatchAsync(PremierLeague.Id, It.IsAny<CancellationToken>())).ReturnsAsync(PremierLeague);
        var ai = new Mock<IForecastAiClient>();

        var response = await CreateGuestClient(odds, ai).GetAsync($"/api/matches/{PremierLeague.Id}/forecast");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ai.Verify(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GuestLogin_ReturnsNotFound_WhenNoGuestUser()
    {
        var response = await factory.WithTestJwt().CreateClient().PostAsync("/api/auth/guest", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GuestLogin_ReturnsTokenForGuestUser_WithoutPassword()
    {
        var testFactory = factory.WithTestJwt();
        await SeedGuestUserAsync(testFactory);

        var client = testFactory.CreateClient();
        var login = await client.PostAsync("/api/auth/guest", null);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.NotNull(me);
        Assert.Equal(GuestUserBootstrapper.DisplayName, me.DisplayName);
        Assert.Contains(RoleNames.Guest, me.Roles);
    }

    [Fact]
    public async Task Refresh_GuestTokenReuse_RejectsOnlyThatToken_OtherGuestSessionsStayOpen()
    {
        var testFactory = factory.WithTestJwt();
        await SeedGuestUserAsync(testFactory);
        var client = testFactory.CreateClient();

        // Aynı paylaşılan misafir hesabıyla açılmış iki ayrı oturum.
        var first = await GuestLoginAsync(client);
        var other = await GuestLoginAsync(client);

        var refreshed = await RefreshAsync(client, first.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var rotated = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;

        // Eski token tekrar gelirse yalnızca bu istek reddedilir...
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, first.RefreshToken)).StatusCode);

        // ...diğer misafirin oturumu ve yenilenmiş zincir açık kalır (normal kullanıcıda ikisi de iptal edilirdi).
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, other.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, rotated.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Forecast_GuestAiLimit_IsCountedPerClientIp()
    {
        var odds = new Mock<IOddsProvider>();
        odds.Setup(o => o.GetMatchAsync(PremierLeague.Id, It.IsAny<CancellationToken>())).ReturnsAsync(PremierLeague);

        var testFactory = factory.WithTestJwt().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(odds.Object);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        }));
        var limit = (testFactory.Services.GetRequiredService<IConfiguration>()
            .GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions()).AiPermitLimit;

        // Bütün misafirler aynı hesabı (aynı "sub") kullanır; yalnızca IP'leri farklıdır.
        var token = testFactory.Services.GetRequiredService<JwtTokenService>().CreateAccessToken(1, "misafir", [RoleNames.Guest]).Token;
        HttpClient GuestFrom(string ip)
        {
            var client = testFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.Header, ip);
            return client;
        }

        var guestA = GuestFrom("203.0.113.1");
        var guestB = GuestFrom("203.0.113.2");
        var url = $"/api/matches/{PremierLeague.Id}/forecast";

        // Süper Lig dışı maç AI çağrılmadan 404 döner ama yine de limite sayılır.
        for (var i = 0; i < limit; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await guestA.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guestA.GetAsync(url)).StatusCode);

        // Aynı misafir hesabı, farklı IP: kendi hakkı vardır (önceden tüm misafirler tek sayacı paylaşıyordu).
        Assert.Equal(HttpStatusCode.NotFound, (await guestB.GetAsync(url)).StatusCode);
    }

    private static async Task SeedGuestUserAsync(WebApplicationFactory<Program> testFactory)
    {
        using var scope = testFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = new Role { Name = RoleNames.Guest };
        var user = new User { UserName = "misafir", Email = "misafir@test.local", DisplayName = GuestUserBootstrapper.DisplayName };
        user.UserRoles.Add(new UserRole { Role = role });
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    private static async Task<TokenResponse> GuestLoginAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/auth/guest", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshToken));

    // TestServer'da tüm istekler aynı (boş) IP'den gelir; testte farklı istemcileri ayırt etmek için IP header'dan atanır.
    // Startup filter olduğu için rate limiter dahil uygulamanın tüm middleware'lerinden önce çalışır.
    private sealed class TestClientIpStartupFilter : IStartupFilter
    {
        public const string Header = "X-Test-Client-IP";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var ip))
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}