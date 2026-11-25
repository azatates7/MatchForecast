namespace MatchForecast.Data.Entity;

/*
 * Her AI çağrısının kaydı (tahmin üretimi, ön ısıtma ve refresh dahil). Başarısız çağrılar da yazılır (ör. 429 kota aşımı).
 * Maliyet kayıt anındaki fiyatla hesaplanıp saklanır: fiyat ayarı sonradan değişse de geçmiş satırlar değişmez.
 * Token sayıları sağlayıcının bildirdiği değerlerdir; başarısız çağrılarda bilinmediği için boştur.
 */
public sealed class AiUsage
{
    public long Id { get; set; }
    public int FixtureId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool Success { get; set; }

    // Başarısız çağrıda dönen HTTP durum kodu (ForecastException.StatusCode); bilinmiyorsa null.
    public int? StatusCode { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int DurationMs { get; set; }
    public decimal EstimatedCostUsd { get; set; }

    public DateTime CreatedAt { get; set; }
}