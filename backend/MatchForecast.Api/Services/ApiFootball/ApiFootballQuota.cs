using System.Globalization;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Email;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.ApiFootball;

/*
 * API-Football günlük istek kotası takibi.
 *
 * API-Football her cevapta günlük kotayı başlıkta bildirir: x-ratelimit-requests-limit (günlük hak) ve
 * x-ratelimit-requests-remaining (kalan). Ayrı bir istek yapılmaz; ApiFootballQuotaHandler, fikstür ve sonuç
 * isteklerinin (iki HttpClient) cevaplarından bu değerleri okuyup buraya verir.
 *
 * - Son değer Redis'te tutulur (birden fazla API instance'ı aynı bilgiyi görür); AI kullanım ekranında gösterilir.
 * - Kalan istek OddsProvider:QuotaWarnPercent yüzdesine inince yöneticiye günde bir kez uyarı maili, kota bitince
 *   bir mail daha gider. "Günde bir kez" Redis kilidiyle sağlanır (TryLockAsync, UTC gününe göre anahtar).
 * - Mail gönderimi isteği bekletmez (arka planda); hata olursa yalnızca loglanır.
 */
public sealed class ApiFootballQuotaTracker(
    IRedisCacheService cache,
    IEmailSender emailSender,
    IOptions<OddsProviderOptions> options,
    IOptions<SmtpOptions> smtpOptions,
    IHostEnvironment environment,
    ILogger<ApiFootballQuotaTracker> logger)
{
    public const string LimitHeader = "x-ratelimit-requests-limit";
    public const string RemainingHeader = "x-ratelimit-requests-remaining";
    private const string Key = "apifootball:quota";

    public Task<ApiFootballQuotaResponse?> GetAsync() => cache.GetAsync<ApiFootballQuotaResponse>(Key);

    public async Task RecordAsync(int limit, int remaining, DateTimeOffset now)
    {
        if (limit <= 0 || remaining < 0)
            return;

        await cache.SetAsync(Key, new ApiFootballQuotaResponse(limit, remaining, now), TimeSpan.FromDays(2));

        var level = WarningLevel(limit, remaining, options.Value.QuotaWarnPercent);
        if (level is null)
            return;

        var smtp = smtpOptions.Value;
        if (!smtp.Enabled || smtp.AdminEmails.Length == 0)
            return;

        // Aynı seviye için günde bir mail (kota UTC günüyle yenilendiği için anahtar UTC tarihli).
        var lockKey = $"apifootball:quota:warned:{now.UtcDateTime:yyyy-MM-dd}:{level}";
        if (!await cache.TryLockAsync(lockKey, TimeSpan.FromHours(26)))
            return;

        _ = Task.Run(() => SendAsync(level, limit, remaining, now));
    }

    // "empty": kota bitti; "low": kalan istek eşik yüzdesinde veya altında; null: uyarı gerekmez.
    public static string? WarningLevel(int limit, int remaining, int warnPercent)
    {
        if (warnPercent <= 0 || limit <= 0)
            return null;
        if (remaining <= 0)
            return "empty";
        return remaining * 100 <= limit * warnPercent ? "low" : null;
    }

    // Başlık değerini okur; yoksa veya sayı değilse null.
    public static int? ReadHeader(HttpResponseMessage res, string name) =>
        res.Headers.TryGetValues(name, out var values)
        && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    private async Task SendAsync(string level, int limit, int remaining, DateTimeOffset now)
    {
        try
        {
            var env = environment.EnvironmentName;
            var subject = level == "empty"
                ? $"[MatchForecast][{env}] API-Football günlük kotası doldu ({limit}/{limit})"
                : $"[MatchForecast][{env}] API-Football günlük kotası azaldı: {remaining}/{limit} kaldı";
            var body = $"""
                API-Football günlük istek kotası
                Zaman   : {now:yyyy-MM-dd HH:mm:ss zzz}
                Kalan   : {remaining} / {limit}

                {(level == "empty"
                    ? "Kota doldu: maç listesi ve sonuç toplama, kota yenilenene kadar API-Football'dan veri alamaz (önbellekteki listeler gösterilmeye devam eder)."
                    : "Kalan istek azaldı. Kota dolarsa maç listesi ve sonuç toplama, kota yenilenene kadar API-Football'dan veri alamaz.")}
                Güncel durum yönetim panelindeki AI kullanım sekmesinde görülebilir. Bu uyarı aynı gün tekrar gönderilmez.
                """;
            await emailSender.SendAsync(smtpOptions.Value.AdminEmails, subject, body, isHtml: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API-Football quota warning e-mail could not be sent.");
        }
    }
}

// API-Football HttpClient'larının cevaplarından kota başlıklarını okur. Okuma hatası isteği hiçbir zaman bozmaz.
public sealed class ApiFootballQuotaHandler(ApiFootballQuotaTracker tracker, ILogger<ApiFootballQuotaHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var res = await base.SendAsync(request, ct);
        try
        {
            var limit = ApiFootballQuotaTracker.ReadHeader(res, ApiFootballQuotaTracker.LimitHeader);
            var remaining = ApiFootballQuotaTracker.ReadHeader(res, ApiFootballQuotaTracker.RemainingHeader);
            if (limit is not null && remaining is not null)
                await tracker.RecordAsync(limit.Value, remaining.Value, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "API-Football quota headers could not be recorded.");
        }
        return res;
    }
}