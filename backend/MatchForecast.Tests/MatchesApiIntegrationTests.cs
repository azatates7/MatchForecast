using System.Net;
using System.Net.Http.Json;
using MatchForecast.Api.Models;
using MatchForecast.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

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
        }).CreateClient();

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
    public async Task GetOdds_ReturnsOk_WhenOddsExist()
    {
        // Arrange
        var mockOdds = new Mock<IOddsProvider>();
        var sampleOdds = new MatchOdds(
            1,
            "MockBookmaker",
            [new Market(1, "Match Winner", [new OddOption("Home", 1.85m), new OddOption("Away", 3.40m)])]
        );
        mockOdds.Setup(o => o.GetOddsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleOdds);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockOdds.Object);
            });
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/matches/1/odds");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var odds = await response.Content.ReadFromJsonAsync<MatchOdds>();
        Assert.NotNull(odds);
        Assert.Equal(1, odds.FixtureId);
        Assert.Equal("MockBookmaker", odds.Bookmaker);
        Assert.Single(odds.Markets);
    }

    [Fact]
    public async Task GetOdds_ReturnsNotFound_WhenOddsDoNotExist()
    {
        // Arrange
        var mockOdds = new Mock<IOddsProvider>();
        mockOdds.Setup(o => o.GetOddsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchOdds?)null);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockOdds.Object);
            });
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/matches/999/odds");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetForecast_ReturnsOk_WhenMatchAndOddsAndAiResponseAreValid()
    {
        // Arrange
        var mockOdds = new Mock<IOddsProvider>();
        var mockAi = new Mock<IForecastAiClient>();

        var sampleMatch = new MatchSummary(10, DateTimeOffset.Now, "La Liga", "Spain", "Real Madrid", "Barcelona", "NS");
        var sampleOdds = new MatchOdds(
            10,
            "BetProvider",
            [new Market(1, "Full Time Result", [new OddOption("Home", 2.10m), new OddOption("Draw", 3.20m), new OddOption("Away", 2.80m)])]
        );

        mockOdds.Setup(o => o.GetMatchAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleMatch);
        mockOdds.Setup(o => o.GetOddsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleOdds);

        var jsonAiResponse = """
        {
            "summary": "Real Madrid home advantage gives them an edge.",
            "predictions": [
                { "optionId": "1.1", "label": "Full Time Result: Home", "confidence": 75.0, "reasoning": "Strong home form." }
            ]
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
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/matches/10/forecast");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ForecastResult>();
        Assert.NotNull(result);
        Assert.Equal("Real Madrid", result.Match.HomeTeam);
        Assert.Equal("Real Madrid home advantage gives them an edge.", result.Summary);
        Assert.Single(result.Predictions);
        Assert.Equal(75, result.Predictions[0].Confidence);
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
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/matches/888/forecast");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
