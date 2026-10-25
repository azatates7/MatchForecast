using System.Net;
using System.Net.Http.Json;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Security;
using MatchForecast.Api.Services.Security.Guest;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
}