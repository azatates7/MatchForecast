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
using MatchForecast.Api.Services.Absences;

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
    private readonly Mock<IForecastStore> _mockStore = new();
    private readonly Mock<IAiUsageStore> _mockUsage = new();
    private readonly Mock<IAbsenceResearcher> _mockAbsences = new();

    private ForecastService CreateService() => new(
        _mockOdds.Object,
        _mockAi.Object,
        _cache,
        _mockStore.Object,
        _mockUsage.Object,
        _mockAbsences.Object,
        Options.Create(new AiOptions { CacheMinutes = 30 }),
        Options.Create(new AbsencesOptions { CacheMinutes = 240 }),
        Options.Create(new ForecastOptions { PredictionCount = 5 }),
        NullLogger<ForecastService>.Instance);

    private void SetupMatch(int id = 1) =>
        _mockOdds.Setup(o => o.GetMatchAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MatchSummary(id, DateTimeOffset.Now, "Serie A", "Italy", "Inter", "Milan", "NS"));

    private void SetupAi(string response) =>
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletion(response));

    [Fact]
    public async Task GetForecastAsync_ReturnsStatsOutcomesAndPredictions()
    {
        SetupMatch();
        SetupAi(ValidAiJson);

        var result = await CreateService().GetForecastAsync(1, false, CancellationToken.None);

        Assert.Equal("Ev sahibi önde.", result.Summary.Tr);
        Assert.Equal(7, result.Stats.Count); // 6 model metriği + hesaplanan toplam gol
        Assert.Equal(100, result.Outcomes.Home + result.Outcomes.Draw + result.Outcomes.Away);
        Assert.Equal(5, result.Predictions.Count);
        Assert.Equal("Ev 0.5 Üst", result.Predictions[0].Label.Tr);
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
        // Mesaj dili CurrentUICulture'a bağlı (Lang.T); test makinenin dilinden bağımsız olsun.
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("tr");

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
    public async Task GetForecastAsync_RecordsAiUsage_WithTokens()
    {
        SetupMatch();
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletion(ValidAiJson, 800, 600));

        await CreateService().GetForecastAsync(1, false, CancellationToken.None);

        _mockUsage.Verify(u => u.RecordAsync(1,
            It.Is<AiCompletion>(c => c.InputTokens == 800 && c.OutputTokens == 600), null,
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetForecastAsync_RecordsFailedAiCall_WithProviderStatusCode()
    {
        SetupMatch();
        // İstemciye 503 döner, kayda sağlayıcının gerçek kodu (429) yazılır.
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForecastException("quota", 503, 429));

        await Assert.ThrowsAsync<ForecastException>(() => CreateService().GetForecastAsync(1, false, CancellationToken.None));

        _mockUsage.Verify(u => u.RecordAsync(1, null, 429, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetForecastAsync_StillReturnsForecast_WhenUsageCannotBeRecorded()
    {
        SetupMatch();
        SetupAi(ValidAiJson);
        _mockUsage.Setup(u => u.RecordAsync(It.IsAny<int>(), It.IsAny<AiCompletion?>(), It.IsAny<int?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await CreateService().GetForecastAsync(1, false, CancellationToken.None);

        Assert.Equal(5, result.Predictions.Count);
    }

    [Fact]
    public async Task GetForecastAsync_SendsAbsencesToAi_AndReturnsThem()
    {
        SetupMatch();
        var report = new AbsenceReport(AbsenceReport.Ready,
            [new TeamAbsence("Inter", "L. Martinez", TeamAbsence.Suspended, LocalizedText.Of("Kırmızı kart cezası", "Red card ban"))],
            [new AbsenceSource("inter.it", "https://example.com/a")], DateTimeOffset.Now);
        _mockAbsences.Setup(a => a.GetAsync(It.IsAny<MatchSummary>(), It.IsAny<CancellationToken>())).ReturnsAsync(report);
        string? userPrompt = null;
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, user, _) => userPrompt = user)
            .ReturnsAsync(new AiCompletion(ValidAiJson));

        var result = await CreateService().GetForecastAsync(1, false, CancellationToken.None);

        Assert.Contains("- Inter: L. Martinez (cezalı, Kırmızı kart cezası)", userPrompt);
        Assert.Contains("- Milan: açıklanan eksik yok", userPrompt);
        Assert.Same(report, result.Absences); // ekranda gösterilmek üzere sonuçta da döner
    }

    [Fact]
    public async Task GetForecastAsync_StillForecasts_WhenAbsencesFail()
    {
        SetupMatch();
        _mockAbsences.Setup(a => a.GetAsync(It.IsAny<MatchSummary>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("search down"));
        string? userPrompt = null;
        _mockAi.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, user, _) => userPrompt = user)
            .ReturnsAsync(new AiCompletion(ValidAiJson));

        var result = await CreateService().GetForecastAsync(1, false, CancellationToken.None);

        Assert.Equal(5, result.Predictions.Count);
        Assert.Contains("Eksik oyuncular: bilgi alınamadı.", userPrompt);
        Assert.Equal(AbsenceReport.Unavailable, result.Absences!.State);
    }

    [Fact]
    public void ForecastCacheTtl_EndsAtMatchDayStart_WhenAbsencesPending()
    {
        var now = new DateTime(2026, 11, 26, 20, 0, 0);
        var match = new MatchSummary(1, new DateTimeOffset(2026, 11, 28, 21, 0, 0, TimeSpan.FromHours(3)), "Süper Lig", "Turkey", "A", "B", "NS");
        var service = CreateServiceWith(cacheMinutes: 1440 * 7);

        // 26.11 20:00 → 28.11 00:00 = 28 saat (yedi günlük ayar yerine)
        Assert.Equal(TimeSpan.FromHours(28), service.ForecastCacheTtl(match, AbsenceReport.PendingReport(), now));
    }

    [Fact]
    public void ForecastCacheTtl_IsLimitedByAbsenceRefresh_OnMatchDay()
    {
        var match = new MatchSummary(1, DateTimeOffset.Now.AddHours(5), "Süper Lig", "Turkey", "A", "B", "NS");
        var service = CreateServiceWith(cacheMinutes: 1440);
        var ready = new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now);

        Assert.Equal(TimeSpan.FromMinutes(240), service.ForecastCacheTtl(match, ready, DateTime.Now));
        Assert.Equal(TimeSpan.FromMinutes(1440), service.ForecastCacheTtl(match, AbsenceReport.UnavailableReport(), DateTime.Now));
    }

    private ForecastService CreateServiceWith(int cacheMinutes) => new(
        _mockOdds.Object, _mockAi.Object, _cache, _mockStore.Object, _mockUsage.Object, _mockAbsences.Object,
        Options.Create(new AiOptions { CacheMinutes = cacheMinutes }),
        Options.Create(new AbsencesOptions { CacheMinutes = 240 }),
        Options.Create(new ForecastOptions { PredictionCount = 5 }),
        NullLogger<ForecastService>.Instance);

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

    [Fact]
    public async Task GetForecastAsync_SavesGeneratedForecastOnce_NotCachedOnes()
    {
        SetupMatch(7);
        SetupAi(ValidAiJson);
        var service = CreateService();

        var result = await service.GetForecastAsync(7, false, CancellationToken.None);
        await service.GetForecastAsync(7, false, CancellationToken.None); // Redis'ten gelir, tekrar kaydedilmez.

        _mockStore.Verify(s => s.SaveAsync(It.Is<ForecastResult>(r => r.Match.Id == 7 && r.GeneratedAt == result.GeneratedAt), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetForecastAsync_ReturnsForecast_WhenStoreFails()
    {
        // Veritabanı hatası kullanıcıya tahmin dönülmesini engellememeli (fail-open).
        SetupMatch(8);
        SetupAi(ValidAiJson);
        _mockStore.Setup(s => s.SaveAsync(It.IsAny<ForecastResult>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await CreateService().GetForecastAsync(8, false, CancellationToken.None);

        Assert.Equal(8, result.Match.Id);
        Assert.NotEmpty(result.Predictions);
    }
}