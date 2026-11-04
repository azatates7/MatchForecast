namespace MatchForecast.Data.Entity;

/*
 * Üretilen tahminin kalıcı kaydı (Redis'teki kayıt Ai:CacheMinutes sonra silinir, bu kalır).
 * Maç başına tek satır: tahmin yeniden üretilirse (cache süresi doldu / Admin refresh) satır güncellenir.
 * ResultJson, API'nin döndüğü ForecastResult'ın kendisidir (jsonb); ileride maç sonuçlarıyla karşılaştırmak için.
 */
public sealed class ForecastRecord
{
    public int Id { get; set; }
    public int FixtureId { get; set; }

    // Maç bilgisi ayrı kolonlarda da tutulur ki tarih/lig bazlı sorgular JSON açmadan yapılabilsin.
    public DateTime Kickoff { get; set; }
    public string Country { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;

    // Tahmini üreten sağlayıcı ve model (Ai:Provider, Ai:{Provider}:Model).
    public string AiProvider { get; set; } = string.Empty;
    public string AiModel { get; set; } = string.Empty;

    public string ResultJson { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}