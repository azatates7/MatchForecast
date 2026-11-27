namespace MatchForecast.Data.Entity;

/*
 * Her AI çağrısının kaydı (tahmin üretimi, ön ısıtma, refresh ve eksik oyuncu araması dahil). Başarısız çağrılar da yazılır (ör. 429 kota aşımı).
 * Maliyet kayıt anındaki fiyatla hesaplanıp saklanır: fiyat ayarı sonradan değişse de geçmiş satırlar değişmez.
 * Token sayıları sağlayıcının bildirdiği değerlerdir; başarısız çağrılarda bilinmediği için boştur.
 */
public sealed class AiUsage
{
    public long Id { get; set; }
    public int FixtureId { get; set; }

    // Çağrının amacı: AiUsagePurpose.Forecast (tahmin üretimi) veya AiUsagePurpose.Absences (eksik oyuncu araması).
    public string Purpose { get; set; } = AiUsagePurpose.Forecast;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool Success { get; set; }

    // Başarısız çağrıda dönen HTTP durum kodu (ForecastException.StatusCode); bilinmiyorsa null.
    public int? StatusCode { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }

    // Google araması (grounding) sorgu sayısı; arama yapmayan çağrılarda 0. Maliyeti EstimatedCostUsd'ye dahildir.
    public int SearchQueries { get; set; }
    public int DurationMs { get; set; }
    public decimal EstimatedCostUsd { get; set; }

    public DateTime CreatedAt { get; set; }
}

public static class AiUsagePurpose
{
    public const string Forecast = "Forecast";
    public const string Absences = "Absences";
}