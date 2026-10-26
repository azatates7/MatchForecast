using MatchForecast.Api.Options;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.Security.Guest;
using MatchForecast.Models.Response;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MatchForecast.Tests;

public class ForecastPrewarmTests
{
    private const string ValidAiJson = """
        {
          "summary": "Dengeli maç.",
          "homeGoals": { "expected": 1.5, "reasoning": "-" },
          "awayGoals": { "expected": 1.1, "reasoning": "-" },
          "firstHalfGoals": { "expected": 1.0, "reasoning": "-" },
          "corners": { "expected": 9.5, "reasoning": "-" },
          "yellowCards": { "expected": 4.2, "reasoning": "-" },
          "redCards": { "expected": 0.2, "reasoning": "-" }
        }
        """;

    private static readonly MatchSummary SuperLig1 =
        new(1, DateTimeOffset.Now, GuestAccess.League, GuestAccess.Country, "Galatasaray", "Fenerbahçe", "NS");

    private static readonly MatchSummary SuperLig2 =
        new(2, DateTimeOffset.Now, GuestAccess.League, GuestAccess.Country, "Beşiktaş", "Trabzonspor", "NS");

    private static readonly MatchSummary PremierLeague =
        new(3, DateTimeOffset.Now, "Premier League", "England", "Arsenal", "Chelsea", "NS");

    private readonly Mock<IOddsProvider> _odds = new();
    private readonly Mock<IForecastAiClient> _ai = new();
    private readonly InMemoryRedisCacheService _cache = new();

    public ForecastPrewarmTests()
    {
        MatchSummary[] all = [SuperLig1, SuperLig2, PremierLeague];
        _odds.Setup(o => o.GetMatchesAsync(GuestAccess.Today, It.IsAny<CancellationToken>())).ReturnsAsync(all);
        foreach (var m in all)
            _odds.Setup(o => o.GetMatchAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);
        _ai.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidAiJson);
    }

    private ForecastPrewarmWorker CreateWorker()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_odds.Object)
            .AddSingleton(_ai.Object)
            .AddSingleton<IRedisCacheService>(_cache)
            .AddSingleton(Options.Create(new AiOptions { CacheMinutes = 30 }))
            .AddSingleton(Options.Create(new ForecastOptions { PredictionCount = 5 }))
            .AddScoped<ForecastService>()
            .BuildServiceProvider();

        return new ForecastPrewarmWorker(
            new ForecastPrewarmQueue(Options.Create(new ForecastOptions())),
            services.GetRequiredService<IServiceScopeFactory>(),
            _cache,
            NullLogger<ForecastPrewarmWorker>.Instance);
    }

    private int AiCalls() => _ai.Invocations.Count(i => i.Method.Name == nameof(IForecastAiClient.CompleteAsync));

    [Fact]
    public async Task PrewarmTodayAsync_GeneratesOnlySuperLigForecasts_AndCachesThem()
    {
        await CreateWorker().PrewarmTodayAsync(CancellationToken.None);

        Assert.Equal(2, AiCalls());
        Assert.NotNull(await _cache.GetAsync<ForecastResult>($"forecast:v2:{SuperLig1.Id}"));
        Assert.NotNull(await _cache.GetAsync<ForecastResult>($"forecast:v2:{SuperLig2.Id}"));
        Assert.Null(await _cache.GetAsync<ForecastResult>($"forecast:v2:{PremierLeague.Id}"));
    }

    [Fact]
    public async Task PrewarmTodayAsync_SkipsWhileLockIsHeld()
    {
        var worker = CreateWorker();

        await worker.PrewarmTodayAsync(CancellationToken.None);
        await worker.PrewarmTodayAsync(CancellationToken.None);

        Assert.Equal(2, AiCalls()); // ikinci çağrı kilit nedeniyle hiç çalışmaz
    }

    [Fact]
    public async Task PrewarmTodayAsync_DoesNotCallAi_ForAlreadyCachedForecasts()
    {
        var worker = CreateWorker();
        await worker.PrewarmTodayAsync(CancellationToken.None);

        // Kilit kalkar (ör. süresi doldu); tahminler hâlâ cache'te olduğu için AI yeniden çağrılmaz.
        await _cache.RemoveAsync($"prewarm:superlig:{GuestAccess.Today:yyyy-MM-dd}");
        await worker.PrewarmTodayAsync(CancellationToken.None);

        Assert.Equal(2, AiCalls());
    }

    [Fact]
    public async Task PrewarmTodayAsync_ContinuesWhenOneMatchFails()
    {
        _odds.Setup(o => o.GetMatchAsync(SuperLig1.Id, It.IsAny<CancellationToken>())).ReturnsAsync((MatchSummary?)null);

        await CreateWorker().PrewarmTodayAsync(CancellationToken.None);

        Assert.Null(await _cache.GetAsync<ForecastResult>($"forecast:v2:{SuperLig1.Id}"));
        Assert.NotNull(await _cache.GetAsync<ForecastResult>($"forecast:v2:{SuperLig2.Id}"));
    }

    [Fact]
    public async Task ConcurrentRequestsForSameMatch_CallAiOnce()
    {
        var gate = new TaskCompletionSource<string>();
        _ai.Setup(a => a.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(gate.Task);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_odds.Object)
            .AddSingleton(_ai.Object)
            .AddSingleton<IRedisCacheService>(_cache)
            .AddSingleton(Options.Create(new AiOptions { CacheMinutes = 30 }))
            .AddSingleton(Options.Create(new ForecastOptions { PredictionCount = 5 }))
            .AddScoped<ForecastService>()
            .BuildServiceProvider();

        // Ön ısıtma ve kullanıcı isteği gibi iki ayrı scope aynı maçı aynı anda ister.
        using var s1 = services.CreateScope();
        using var s2 = services.CreateScope();
        var first = s1.ServiceProvider.GetRequiredService<ForecastService>().GetForecastAsync(SuperLig1.Id, false, CancellationToken.None);
        var second = s2.ServiceProvider.GetRequiredService<ForecastService>().GetForecastAsync(SuperLig1.Id, false, CancellationToken.None);

        gate.SetResult(ValidAiJson);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, AiCalls());
        Assert.Equal(results[0].GeneratedAt, results[1].GeneratedAt);
    }

    [Fact]
    public async Task Queue_IgnoresRequests_WhenDisabled()
    {
        var queue = new ForecastPrewarmQueue(Options.Create(new ForecastOptions { PrewarmOnLogin = false }));
        queue.Request();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var received = 0;
        try
        {
            await foreach (var _ in queue.ReadAllAsync(cts.Token))
                received++;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(0, received);
    }

    [Fact]
    public async Task Queue_CollapsesManyRequestsIntoOne()
    {
        var queue = new ForecastPrewarmQueue(Options.Create(new ForecastOptions()));
        for (var i = 0; i < 50; i++)
            queue.Request();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var received = 0;
        try
        {
            await foreach (var _ in queue.ReadAllAsync(cts.Token))
                received++;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(1, received);
    }
}