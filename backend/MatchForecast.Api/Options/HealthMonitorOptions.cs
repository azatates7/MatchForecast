namespace MatchForecast.Api.Options;

/*
 * Uygulama içi sağlık izleme (HealthMonitorService). /healthz/ready'deki kontrolleri (veritabanı, Redis, Kafka) düzenli çalıştırır; bir bileşenin durumu değişince (ör. Kafka erişilemez oldu / düzeldi) yöneticiye mail atar (Smtp:AdminEmails).
 * - IntervalSeconds: kontrol aralığı (en az 30). Varsayılan 120.
 * - ConsecutiveChecks: yeni durumun mail için kaç ardışık kontrolde aynı kalması gerektiği. Anlık dalgalanmalar mail üretmesin diye; varsayılan 2 (yani yaklaşık 2–4 dk sonra bildirilir).
 * Sunucu veya container tamamen kapanırsa bu servis de çalışmaz; o durum için dışarıdan izleme (uptime servisi) gerekir.
 */
public sealed class HealthMonitorOptions
{
    public const string Section = "HealthMonitor";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 120;
    public int ConsecutiveChecks { get; set; } = 2;
}