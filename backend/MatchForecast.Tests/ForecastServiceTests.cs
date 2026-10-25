using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;

namespace MatchForecast.Tests;

public class ForecastServiceTests
{
    private const string ValidAiJson = """
        {
          "summary": "Ev sahibi önde.",
          "homeGoals": { "expected": 1.6, "reasoning": "Ev sahibi içeride etkili." },
          "awayGoals": { "expected": 1.0, "reasoning": "Deplasman hücumu zayıf." },
          "firstHalfGoals": { "expected": 1.1, "reasoning": "İlk yarı temkinli." },
          "corners": { "expected": 10.2, "reasoning": "Kanat oyunu." },
          "yellowCards": { "expected": 4.6, "reasoning": "Sert maç." },
          "redCards": { "expected": 0.2, "reasoning": "Lig ortalaması." }
        }
        """;

    private readonly Mock<IOddsProvider> _mockOdds = new();
    private readonly Mock<IForecastAiClient> _mockAi = new();
    private readonly IRedisCacheService _cache = new InMemoryRedisCacheService();

    private ForecastService CreateService() => new(
        _mockOdds.Object,
        _mockAi.Object,
        _cache,
        Options.Create(new AiOptions { CacheMinutes = 30 }),
        Options.Create(new ForecastOptions { PredictionCount = 5 }),
        NullLogger<ForecastService>.Instance);

    private void SetupMatch(int id = 1) =>
        _mockOdds.Setup(o => o.GetMatchAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MatchSummary(id, DateTimeOffset.Now, "Serie A", "Italy", "Inter", "Milan", "NS"));

    private void SetupAi(string response) =>
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    [Fact]
    public async Task GetForecastAsync_ReturnsStatsOutcomesAndPredictions()
    {
        SetupMatch();
        SetupAi(ValidAiJson);

        var result = await CreateService().GetForecastAsync(1, false, CancellationToken.None);

        Assert.Equal("Ev sahibi önde.", result.Summary);
        Assert.Equal(7, result.Stats.Count); // 6 model metriği + hesaplanan toplam gol
        Assert.Equal(100, result.Outcomes.Home + result.Outcomes.Draw + result.Outcomes.Away);
        Assert.Equal(5, result.Predictions.Count);
        Assert.Equal("Ev 0.5 Üst", result.Predictions[0].Label);
    }

    [Fact]
    public async Task GetForecastAsync_SendsMatchInfoWithoutOptionList()
    {
        SetupMatch();
        SetupAi(ValidAiJson);

        await CreateService().GetForecastAsync(1, false, CancellationToken.None);

        _mockAi.Verify(a => a.CompleteAsync(
            It.IsAny<string>(),
            It.Is<string>(u => u.Contains("Inter (ev)") && !u.Contains("optionId")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetForecastAsync_ThrowsForecastException_WhenAiReturnsInvalidJson()
    {
        SetupMatch();
        SetupAi("Sorry, I cannot produce JSON right now.");

        var ex = await Assert.ThrowsAsync<ForecastException>(() => CreateService().GetForecastAsync(1, false, CancellationToken.None));
        Assert.Contains("JSON içermiyor", ex.Message);
    }

    [Fact]
    public async Task GetForecastAsync_ThrowsForecastException_WhenGoalExpectationMissing()
    {
        SetupMatch();
        SetupAi("""{ "summary": "x", "awayGoals": { "expected": 1.0 } }""");

        await Assert.ThrowsAsync<ForecastException>(() => CreateService().GetForecastAsync(1, false, CancellationToken.None));
    }

    [Fact]
    public async Task GetForecastAsync_Throws404_WhenMatchNotFound()
    {
        _mockOdds.Setup(o => o.GetMatchAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((MatchSummary?)null);

        var ex = await Assert.ThrowsAsync<ForecastException>(() => CreateService().GetForecastAsync(99, false, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task GetForecastAsync_UsesCachedResult_WhenRefreshIsFalse()
    {
        SetupMatch(5);
        SetupAi(ValidAiJson);
        var service = CreateService();

        var first = await service.GetForecastAsync(5, false, CancellationToken.None);
        var second = await service.GetForecastAsync(5, false, CancellationToken.None);

        // Redis JSON sakladığı için aynı instance değil, aynı içerik döner.
        Assert.Equal(first.GeneratedAt, second.GeneratedAt);
        Assert.Equal(first.Stats.Count, second.Stats.Count);
        _mockAi.Verify(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}