using MatchForecast.Api.Options;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Accuracy;

/*
 * Tahmini yapılmış ve bitmiş olması gereken maçların sonucunu toplar (match_results), isabet raporu bunu kullanır.
 * - Aday: başlama saatinin üzerinden 150 dk geçmiş, Accuracy:LookbackDays'ten eski olmayan ve sonucu olmayan tahminler
 *   (en eski önce). Daha eskiler için vazgeçilir (ör. canlı takibi olmayan, hep "NS" görünen ligler).
 * - Bir turda en fazla Accuracy:MaxRequestsPerRun istek, her istekte 20 maç.
 * - Bitmiş maç (FT/AET/PEN) skoruyla, oynanmayacak maç (PST/CANC/ABD/AWD/WO) skorsuz kaydedilir; diğerleri (hâlâ canlı,
 *   "NS") kaydedilmez, sonraki turda tekrar sorulur.
 * - Fail-open: API-Football hatası (kota vb.) loglanır, tur atlanır; uygulamanın geri kalanı etkilenmez.
 * - API-Football anahtarı yoksa (testler, yerel geliştirme) hiçbir şey yapılmaz.
 */
public sealed class ResultSettlementWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AccuracyOptions> options,
    IOptions<OddsProviderOptions> oddsOptions,
    ILogger<ResultSettlementWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SettleAfterKickoff = TimeSpan.FromMinutes(150);

    private static readonly HashSet<string> Final = new(StringComparer.OrdinalIgnoreCase) { "FT", "AET", "PEN" };
    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase) { "PST", "CANC", "ABD", "AWD", "WO" };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled || string.IsNullOrWhiteSpace(oddsOptions.Value.ApiKey))
        {
            logger.LogInformation("Maç sonucu toplama kapalı (Accuracy:Enabled veya OddsProvider:ApiKey).");
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, opt.IntervalMinutes)));
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Uygulama kapanıyor.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var (requested, saved) = await SettleAsync(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<IMatchResultProvider>(),
                options.Value, DateTime.Now, ct);
            if (requested > 0)
                logger.LogInformation("Maç sonucu toplama: {Requested} maç soruldu, {Saved} sonuç kaydedildi.", requested, saved);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Maç sonucu toplama başarısız oldu; sonraki turda tekrar denenecek.");
        }
    }

    // Test edilebilsin diye zaman ve bağımlılıklar parametre. Dönüş: sorulan maç sayısı, kaydedilen sonuç sayısı.
    public static async Task<(int Requested, int Saved)> SettleAsync(
        AppDbContext db, IMatchResultProvider provider, AccuracyOptions opt, DateTime now, CancellationToken ct)
    {
        var settleBefore = now - SettleAfterKickoff;
        var lookbackFrom = now.AddDays(-Math.Max(1, opt.LookbackDays));
        var take = Math.Max(1, opt.MaxRequestsPerRun) * ApiFootballResultProvider.MaxIdsPerRequest;

        var pending = await db.Forecasts.AsNoTracking()
            .Where(f => f.Kickoff <= settleBefore && f.Kickoff >= lookbackFrom)
            .Where(f => !db.MatchResults.Any(r => r.FixtureId == f.FixtureId))
            .OrderBy(f => f.Kickoff)
            .Select(f => f.FixtureId)
            .Take(take)
            .ToListAsync(ct);

        var saved = 0;
        foreach (var batch in pending.Chunk(ApiFootballResultProvider.MaxIdsPerRequest))
        {
            var results = await provider.GetResultsAsync(batch, ct);
            foreach (var r in results.Where(r => batch.Contains(r.FixtureId)))
            {
                var final = Final.Contains(r.Status) && r.HomeGoals is not null && r.AwayGoals is not null;
                if (!final && !Void.Contains(r.Status))
                    continue;

                db.MatchResults.Add(new MatchResult
                {
                    FixtureId = r.FixtureId,
                    Status = r.Status,
                    HomeGoals = final ? r.HomeGoals : null,
                    AwayGoals = final ? r.AwayGoals : null,
                    HalfTimeHomeGoals = final ? r.HalfTimeHomeGoals : null,
                    HalfTimeAwayGoals = final ? r.HalfTimeAwayGoals : null,
                    CreatedAt = now
                });
                saved++;
            }

            // Her istekten sonra kaydedilir: sonraki istek kotaya takılırsa alınan sonuçlar kaybolmasın.
            await db.SaveChangesAsync(ct);
        }

        return (pending.Count, saved);
    }
}