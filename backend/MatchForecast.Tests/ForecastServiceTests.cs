using MatchForecast.Api.Models;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MatchForecast.Tests;

public class ForecastServiceTests
{
    private readonly Mock<IOddsProvider> _mockOdds = new();
    private readonly Mock<IForecastAiClient> _mockAi = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly IOptions<AiOptions> _aiOptions = Options.Create(new AiOptions { CacheMinutes = 30 });

    private ForecastService CreateService()
    {
        return new ForecastService(
            _mockOdds.Object,
            _mockAi.Object,
            _cache,
            _aiOptions,
            NullLogger<ForecastService>.Instance);
    }

    [Fact]
    public async Task GetForecastAsync_ThrowsForecastException_WhenOddsNullOrEmpty()
    {
        // Arrange
        var service = CreateService();
        var match = new MatchSummary(1, DateTimeOffset.Now, "Serie A", "Italy", "Inter", "Milan", "NS");
        _mockOdds.Setup(o => o.GetMatchAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _mockOdds.Setup(o => o.GetOddsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((MatchOdds?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForecastException>(() => service.GetForecastAsync(1, false, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
        Assert.Contains("oran yayınlanmamış", ex.Message);
    }

    [Fact]
    public async Task GetForecastAsync_ThrowsForecastException_WhenAiReturnsInvalidJson()
    {
        // Arrange
        var service = CreateService();
        var match = new MatchSummary(1, DateTimeOffset.Now, "Serie A", "Italy", "Inter", "Milan", "NS");
        var odds = new MatchOdds(1, "Bookie", [new Market(1, "1X2", [new OddOption("1", 2.0m)])]);

        _mockOdds.Setup(o => o.GetMatchAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _mockOdds.Setup(o => o.GetOddsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(odds);
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Sorry, I cannot produce JSON right now.");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForecastException>(() => service.GetForecastAsync(1, false, CancellationToken.None));
        Assert.Contains("JSON içermiyor", ex.Message);
    }

    [Fact]
    public async Task GetForecastAsync_FiltersInvalidOptionIds_AndClampsConfidence()
    {
        // Arrange
        var service = CreateService();
        var match = new MatchSummary(1, DateTimeOffset.Now, "Bundesliga", "Germany", "Bayern", "Dortmund", "NS");
        var odds = new MatchOdds(1, "Bookie", [
            new Market(1, "Match Winner", [new OddOption("Home", 1.50m), new OddOption("Away", 5.00m)])
        ]);

        _mockOdds.Setup(o => o.GetMatchAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _mockOdds.Setup(o => o.GetOddsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(odds);

        // OptionId "99.9" does not exist in the prompt index. "1.1" exists. Confidence 150.0 should be clamped to 100.
        var jsonResponse = """
        {
            "summary": "High scoring match expected.",
            "predictions": [
                { "optionId": "99.9", "label": "Invalid Bet", "confidence": 90.0, "reasoning": "Fake bet" },
                { "optionId": "1.1", "label": "Bayern Win", "confidence": 150.0, "reasoning": "Dominant home form" }
            ]
        }
        """;

        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        // Act
        var result = await service.GetForecastAsync(1, false, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Predictions);
        Assert.Equal("Bayern Win", result.Predictions[0].Label);
        Assert.Equal(100, result.Predictions[0].Confidence); // Clamped from 150 to 100
        Assert.Equal(1.50m, result.Predictions[0].Odd);
    }

    [Fact]
    public async Task GetForecastAsync_UsesCachedResult_WhenRefreshIsFalse()
    {
        // Arrange
        var service = CreateService();
        var match = new MatchSummary(5, DateTimeOffset.Now, "Ligue 1", "France", "PSG", "Marseille", "NS");
        var odds = new MatchOdds(5, "Bookie", [new Market(1, "1X2", [new OddOption("Home", 1.40m)])]);

        _mockOdds.Setup(o => o.GetMatchAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _mockOdds.Setup(o => o.GetOddsAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(odds);
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"summary": "Cached analysis", "predictions": [{"optionId": "1.1", "confidence": 80.0}]}""");

        // Act 1: Initial call populates cache
        var first = await service.GetForecastAsync(5, false, CancellationToken.None);

        // Act 2: Second call should return cached object without invoking AI again
        var second = await service.GetForecastAsync(5, false, CancellationToken.None);

        // Assert
        Assert.Same(first, second);
        _mockAi.Verify(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
