using System.Net;
using System.Net.Http.Json;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using MatchForecast.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;

namespace MatchForecast.Tests;

public class MatchesApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MatchesApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMatches_ReturnsOkWithMatchSummaries()
    {
        // Arrange
        var mockOdds = new Mock<IOddsProvider>();
        var sampleMatch = new MatchSummary(1, DateTimeOffset.Now, "Premier League", "England", "Arsenal", "Chelsea", "NS");
        mockOdds.Setup(o => o.GetMatchesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([sampleMatch]);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockOdds.Object);
            });
        }).CreateAuthorizedClient();

        // Act
        var response = await client.GetAsync("/api/matches?date=2026-09-25");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var matches = await response.Content.ReadFromJsonAsync<List<MatchSummary>>();
        Assert.NotNull(matches);
        Assert.Single(matches);
        Assert.Equal("Arsenal", matches[0].HomeTeam);
        Assert.Equal("Chelsea", matches[0].AwayTeam);
    }

    [Fact]
    public async Task GetPopularMatches_ReturnsOkWithTopPopularMatches()
    {
        // Arrange
        var mockOdds = new Mock<IOddsProvider>();
        var sampleMatch = new MatchSummary(100, DateTimeOffset.Now, "Süper Lig", "Turkey", "Galatasaray", "Fenerbahçe", "NS");
        mockOdds.Setup(o => o.GetPopularMatchesAsync(It.IsAny<DateOnly>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([sampleMatch]);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockOdds.Object);
            });
        }).CreateAuthorizedClient();

        // Act
        var response = await client.GetAsync("/api/matches/popular?count=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var matches = await response.Content.ReadFromJsonAsync<List<MatchSummary>>();
        Assert.NotNull(matches);
        Assert.Single(matches);
        Assert.Equal("Galatasaray", matches[0].HomeTeam);
        Assert.Equal("Fenerbahçe", matches[0].AwayTeam);
    }

    [Fact]
    public async Task GetForecast_ReturnsOk_WithStatsAndPredictions()
    {
        // Arrange
        var mockOdds = new Mock<IOddsProvider>();
        var mockAi = new Mock<IForecastAiClient>();

        var sampleMatch = new MatchSummary(10, DateTimeOffset.Now, "La Liga", "Spain", "Real Madrid", "Barcelona", "NS");
        mockOdds.Setup(o => o.GetMatchAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleMatch);

        var jsonAiResponse = """
        {
            "summary": "Real Madrid home advantage gives them an edge.",
            "homeGoals": { "expected": 1.7, "reasoning": "Strong home form." },
            "awayGoals": { "expected": 1.2, "reasoning": "Dangerous on the break." },
            "firstHalfGoals": { "expected": 1.2, "reasoning": "Open game." },
            "corners": { "expected": 9.8, "reasoning": "Both teams attack wide." },
            "yellowCards": { "expected": 5.1, "reasoning": "El Clasico intensity." },
            "redCards": { "expected": 0.3, "reasoning": "Heated derby." }
        }
        """;

        mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonAiResponse);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockOdds.Object);
                services.AddSingleton(mockAi.Object);
            });
        }).CreateAuthorizedClient();

        // Act
        var response = await client.GetAsync("/api/matches/10/forecast");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ForecastResult>();
        Assert.NotNull(result);
        Assert.Equal("Real Madrid", result.Match.HomeTeam);
        Assert.Equal("Real Madrid home advantage gives them an edge.", result.Summary);
        Assert.Equal(5, result.Predictions.Count);
        Assert.Contains(result.Stats, s => s.Metric == StatMetric.Corners && s.Lines.Count == 0);
        Assert.Equal(100, result.Outcomes.Home + result.Outcomes.Draw + result.Outcomes.Away);
    }

    [Fact]
    public async Task GetForecast_ReturnsNotFound_WhenMatchDoesNotExist()
    {
        // Arrange
        var mockOdds = new Mock<IOddsProvider>();
        mockOdds.Setup(o => o.GetMatchAsync(888, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchSummary?)null);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockOdds.Object);
            });
        }).CreateAuthorizedClient();

        // Act
        var response = await client.GetAsync("/api/matches/888/forecast");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}