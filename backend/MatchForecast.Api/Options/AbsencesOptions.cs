namespace MatchForecast.Api.Options;

/*
 * Eksik oyuncu araştırması (Absences bölümü). Gemini'nin Google araması (grounding) ile maç günü resmi açıklamalar taranır.
 * - Enabled: Kapalıyken arama yapılmaz, rapor "Unavailable" döner. Google araması Gemini'nin ÜCRETLİ katmanını ister
 *   (ücretsiz katmanda 400 döner); faturalandırma açılmadan true yapılırsa her deneme "Unavailable" ile sonuçlanır.
 * - Model: Boşsa Ai:Gemini:Model kullanılır. Anahtar ve adres her zaman Ai:Gemini'den alınır (tahmin sağlayıcısı başka olsa da).
 * - CacheMinutes: Maç günü aynı maç için aramanın tekrarlanma aralığı. Kadro bilgisi maça yaklaştıkça netleştiği için
 *   tahmin önbelleği de maç günü bu süreyi aşmaz (tahmin yeni eksik listesiyle yeniden üretilir).
 * - SearchPricePer1K / FreeSearchesPerMonth: Google araması maliyeti (AI kullanım raporu için). Gemini 3 modelleri her arama sorgusunu ayrı faturalar; ayın ilk FreeSearchesPerMonth sorgusu ücretsizdir. Token maliyeti ayrıca
 *   Ai:Gemini:InputPricePer1M / OutputPricePer1M ile hesaplanır. Fiyat değişirse yalnızca bu ayarlar güncellenir.
 */
public sealed class AbsencesOptions
{
    public const string Section = "Absences";

    public bool Enabled { get; set; }
    public string Model { get; set; } = "";
    public int CacheMinutes { get; set; } = 240;
    public decimal SearchPricePer1K { get; set; } = 14m;
    public int FreeSearchesPerMonth { get; set; } = 5000;
}