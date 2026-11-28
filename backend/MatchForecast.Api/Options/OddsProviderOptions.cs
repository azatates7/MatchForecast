namespace MatchForecast.Api.Options;

/*
 * - QuotaWarnPercent: API-Football günlük kotasından kalan istek bu yüzdeye (veya altına) inince yöneticiye günde bir kez uyarı maili gider; kota tamamen bitince ayrıca bir mail daha. 0 = uyarı yok. Varsayılan 20 (ücretsiz planda 20 istek).
 */
public sealed class OddsProviderOptions
{
    public const string Section = "OddsProvider";

    public string BaseUrl { get; set; } = "https://v3.football.api-sports.io/";
    public string ApiKey { get; set; } = string.Empty;
    public string Timezone { get; set; } = "Europe/Istanbul";
    public int CacheMinutes { get; set; } = 30;
    public int QuotaWarnPercent { get; set; } = 20;
}