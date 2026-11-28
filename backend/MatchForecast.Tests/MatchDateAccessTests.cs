using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.Security;
using MatchForecast.Api.Services.Security.Guest;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MatchForecast.Tests;

// Maç listesinde seçilebilen tarihler (normal kullanıcı: bugün ve yarın) ve "analiz edildi" işareti.
public class MatchDateAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly MatchSummary Sample = new(7, DateTimeOffset.Now, "Premier League", "England", "Arsenal", "Chelsea", "NS");
    private static readonly MatchSummary Other = new(8, DateTimeOffset.Now, "Premier League", "England", "Liverpool", "Everton", "NS");

    private static string Day(int offset) => GuestAccess.Today.AddDays(offset).ToString("yyyy-MM-dd");

    private (HttpClient Client, IServiceProvider Services) Create(params string[] roles)
    {
        var odds = new Mock<IOddsProvider>();
        odds.Setup(o => o.GetMatchesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([Sample, Other]);
        odds.Setup(o => o.GetPopularMatchesAsync(It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Sample]);

        var testFactory = factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(odds.Object)))
            .WithTestJwt();
        var client = testFactory.CreateClient();
        var token = testFactory.Services.GetRequiredService<JwtTokenService>().CreateAccessToken(1, "test", roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return (client, testFactory.Services);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task User_CanViewTodayAndTomorrow(int offset)
    {
        var (client, _) = Create(RoleNames.User);

        var response = await client.GetAsync($"/api/matches?date={Day(offset)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task User_CannotViewOtherDates(int offset)
    {
        var (client, _) = Create(RoleNames.User);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/matches?date={Day(offset)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/matches/popular?date={Day(offset)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/matches/analyzed?date={Day(offset)}")).StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task Admin_CanViewAnyDate(int offset)
    {
        var (client, _) = Create(RoleNames.Admin);

        var response = await client.GetAsync($"/api/matches?date={Day(offset)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Analyzed_ReturnsOnlyMatchesWithCachedForecast()
    {
        var (client, services) = Create(RoleNames.User);
        await services.GetRequiredService<IRedisCacheService>().SetAsync(ForecastService.CacheKey(Sample.Id), "x");

        var ids = await client.GetFromJsonAsync<List<int>>($"/api/matches/analyzed?date={Day(0)}");

        Assert.Equal(new[] { Sample.Id }, ids);
    }
}