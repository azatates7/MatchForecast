using MatchForecast.Data.Configuration;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Services.Security;

/*
 * Kullanılamaz hale gelmiş token kayıtlarını günde bir siler; aksi halde tablolar sürekli büyür (misafir girişi şifresiz
 * olduğu için her misafir girişi yeni bir refresh token satırı ekler).
 * - Refresh token: yalnızca süresi dolmuş olanlar. İptal edilmiş ama süresi dolmamış kayıtlar bilerek tutulur: tekrar
 *   kullanılırlarsa çalınma tespiti (reuse detection, bkz. AuthService.RefreshAsync) bu kayda dayanır. Süresi dolunca zaten
 *   reddedilirler, o zaman silinirler.
 * - Şifre sıfırlama: kullanılmış veya süresi dolmuş olanlar (ikisi de tekrar kullanılamaz).
 * İlk temizlik açılıştan kısa süre sonra yapılır (açılışı yavaşlatmasın). Hata olursa loglanır, bir sonraki turda tekrar denenir.
 */
public sealed class TokenCleanUpWorker(IServiceScopeFactory scopeFactory, ILogger<TokenCleanUpWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
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
            // DbContext scoped; singleton worker her turda kendi scope'unu açar.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (refreshTokens, resetTokens) = await CleanupAsync(db, DateTime.Now, ct);
            logger.LogInformation("Token temizliği: {RefreshTokens} refresh token, {ResetTokens} şifre sıfırlama kaydı silindi.",
                refreshTokens, resetTokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hata dışarı kaçarsa BackgroundService host'u durdurur; temizlik kritik değil, yalnızca loglanır.
            logger.LogWarning(ex, "Token temizliği başarısız oldu.");
        }
    }

    // Uygulamanın geri kalanı gibi DateTime.Now (İstanbul saati) kullanılır; değerler DB'ye aynı dönüştürücüyle gider.
    public static async Task<(int RefreshTokens, int ResetTokens)> CleanupAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var expiredRefresh = db.RefreshTokens.Where(x => x.ExpiresAt <= now);
        var unusableReset = db.PasswordResetTokens.Where(x => x.UsedAt != null || x.ExpiresAt <= now);

        if (db.Database.IsRelational())
            return (await expiredRefresh.ExecuteDeleteAsync(ct), await unusableReset.ExecuteDeleteAsync(ct));

        // Testlerdeki bellek içi sağlayıcı ExecuteDelete desteklemez; kayıtlar yüklenip silinir.
        var refresh = await expiredRefresh.ToListAsync(ct);
        var reset = await unusableReset.ToListAsync(ct);
        db.RefreshTokens.RemoveRange(refresh);
        db.PasswordResetTokens.RemoveRange(reset);
        await db.SaveChangesAsync(ct);
        return (refresh.Count, reset.Count);
    }
}