using System.Threading.Channels;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.Security.Guest;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Forecast;

/*
 * Giriş anında günün Türkiye Süper Lig tahminlerini arka planda önceden üretir (cache ısıtma).
 * - Tahmin cache'i (forecast:v2:{id}) kullanıcıdan bağımsızdır; bir kez üretilen tahmin tüm kullanıcılara cache'ten döner.
 * - AuthService her token üretiminde (giriş, kayıt, Google, misafir, refresh) Request() çağırır. Kuyruk kapasitesi 1 olduğundan
 *   aynı anda gelen çok sayıda giriş tek işe indirgenir; giriş isteği hiç beklemez.
 * - Redis kilidi (prewarm:superlig:{tarih}) LockDuration boyunca tekrar çalışmayı engeller; birden fazla API instance'ında da geçerlidir.
 *   Redis'e ulaşılamazsa kilit alınamaz ve ısıtma atlanır: cache yokken üretilen tahmin saklanamaz, AI çağrısı boşa gider.
 * - Maçlar sırayla işlenir (AI'a aynı anda tek istek). Cache'te olan tahmin için AI çağrılmaz; kullanıcı kotasından (AiPolicy) düşmez.
 * - Forecast:PrewarmOnLogin=false ile kapatılır.
 */
public sealed class ForecastPrewarmQueue(IOptions<ForecastOptions> options)
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    // Beklemez, hata fırlatmaz; kuyrukta zaten bekleyen bir istek varsa yenisi düşer.
    public void Request()
    {
        if (options.Value.PrewarmOnLogin)
            _channel.Writer.TryWrite(true);
    }

    public IAsyncEnumerable<bool> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public sealed class ForecastPrewarmWorker(
    ForecastPrewarmQueue queue,
    IServiceScopeFactory scopeFactory,
    IRedisCacheService cache,
    ILogger<ForecastPrewarmWorker> logger) : BackgroundService
{
    // Bu süre içinde gelen girişler ısıtmayı yeniden başlatmaz. Süresi dolan tahminler sonraki ısıtmada yenilenir.
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var _ in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await PrewarmTodayAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Hata dışarı kaçarsa BackgroundService host'u durdurur; ısıtma kritik değil, yalnızca loglanır.
                logger.LogWarning(ex, "Forecast prewarm failed.");
            }
        }
    }

    public async Task PrewarmTodayAsync(CancellationToken ct)
    {
        var today = GuestAccess.Today;
        if (!await cache.TryLockAsync($"prewarm:superlig:{today:yyyy-MM-dd}", LockDuration))
            return;

        // ForecastService ve typed HttpClient'lar scoped; singleton worker her çalışmada kendi scope'unu açar.
        using var scope = scopeFactory.CreateScope();
        var odds = scope.ServiceProvider.GetRequiredService<IOddsProvider>();
        var forecasts = scope.ServiceProvider.GetRequiredService<ForecastService>();

        var matches = GuestAccess.Filter(await odds.GetMatchesAsync(today, ct));
        var warmed = 0;

        foreach (var match in matches)
        {
            try
            {
                await forecasts.GetForecastAsync(match.Id, refresh: false, ct);
                warmed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Prewarm failed for fixture {Id} ({Home} vs {Away})", match.Id, match.HomeTeam, match.AwayTeam);
            }
        }

        logger.LogInformation("Forecast prewarm for {Date}: {Warmed}/{Total} Süper Lig matches ready.", today, warmed, matches.Count);
    }
}