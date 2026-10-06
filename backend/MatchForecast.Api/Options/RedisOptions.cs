namespace MatchForecast.Api.Options;

public sealed class RedisOptions
{
    public const string Section = "Redis";

    public string ConnectionString { get; set; } = string.Empty;

    // Tüm key'lerin öneki; Cache Temizle yalnızca bu önekle başlayan key'leri siler, boş bırakılamaz.
    public string InstanceName { get; set; } = "MatchForecast:";

    // SetAsync'e süre verilmezse kullanılan varsayılan TTL.
    public int CacheMinutes { get; set; } = 30;
}