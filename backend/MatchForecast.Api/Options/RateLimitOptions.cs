namespace MatchForecast.Api.Options;

/*
 * Rate limiting ayarları (RateLimiting bölümü). Sayaçlar bellekte tutulur: uygulama yeniden başlayınca sıfırlanır ve
 * birden fazla API instance'ı çalışıyorsa her biri kendi sayacını tutar (tek sunuculu kurulum için yeterli).
 *
 * - Token (GET /api/auth/token): İstemci IP'si başına sabit pencere. Frontend token'ı sayfa açılışında bir kez,
 *   401 alınca da bir kez daha ister; dakikada 10 istek normal kullanıcıya hiç takılmaz, token toplayan bir botu yavaşlatır.
 * - CacheClear (DELETE /api/cache): IP'den bağımsız, TÜM istemciler için ortak sayaç. Kayan pencere kullanılır:
 *   "herhangi bir 60 dakikalık süre içinde en fazla 2 kez". Sabit pencerede saat sınırında (ör. 10:59'da 2, 11:00'da 2)
 *   art arda 4 temizleme yapılabilirdi; kayan pencere bunu engeller.
 */
public sealed class RateLimitOptions
{
    public const string Section = "RateLimiting";

    // [EnableRateLimiting] attribute'larında kullanılan policy adları.
    public const string TokenPolicy = "token";
    public const string CacheClearPolicy = "cache-clear";
    public const string AiPolicy = "ai";

    public int TokenPermitLimit { get; set; } = 10;
    public int TokenWindowSeconds { get; set; } = 60;

    public int CacheClearPermitLimit { get; set; } = 2;
    public int CacheClearWindowMinutes { get; set; } = 60;

    // AI tahmini üreten endpoint'ler: kullanıcı başına (token'daki "sub"), sabit pencere. Cache'ten dönen istekler de sayılır.
    // Kayıt herkese açık olduğu için bir hesabın AI faturasını şişirmesini engeller.
    public int AiPermitLimit { get; set; } = 30;
    public int AiWindowMinutes { get; set; } = 10;

    // Cloudflare arkasında gerçek istemci IP'si CF-Connecting-IP header'ındadır. YALNIZCA API'ye sadece Cloudflare üzerinden
    // erişilebiliyorsa (ör. Cloudflare Tunnel) true yapılmalı; aksi halde istemci bu header'ı kendisi gönderip limiti atlatabilir.
    public bool TrustCloudflareHeader { get; set; }
}