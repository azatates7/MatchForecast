namespace MatchForecast.Api.Options;

/*
 * Eksik oyuncu araştırması (Absences bölümü). Gemini'nin Google araması (grounding) ile maç günü resmi açıklamalar taranır.
 * - Enabled: Kapalıyken arama yapılmaz, rapor "Unavailable" döner. Google araması Gemini'nin ÜCRETLİ katmanını ister
 *   (ücretsiz katmanda 400 döner); faturalandırma açılmadan true yapılırsa her deneme "Unavailable" ile sonuçlanır.
 * - Model: Boşsa Ai:Gemini:Model kullanılır. Anahtar ve adres her zaman Ai:Gemini'den alınır (tahmin sağlayıcısı başka olsa da).
 * - PreliminaryDaysAhead: Maç gününden önce, maça en fazla bu kadar gün kalmışsa da arama yapılır ve rapor "Preliminary" (ön bilgi) olarak döner; o güne kadar açıklanan ceza ve sakatlıklar gösterilir, maç günü liste ayrıca taranır. 0 = yalnızca maç günü arama (eski davranış). Varsayılan 30: pratikte maç günü öncesi her maç (ücretsiz API-Football planı zaten en fazla yarını gösterir).
 * - PreliminaryCacheHours: Ön bilginin yenilenme aralığı (en geç maç gününün başında, 00:00, biter). Maç birkaç gün sonraysa liste günde bir güncellenir; bu aralıktaki her yenileme bir arama demektir. Varsayılan 24.
 * - CacheMinutes: Maç günü aynı maç için aramanın tekrarlanma aralığı. Kadro bilgisi maça yaklaştıkça netleştiği için
 *   tahmin önbelleği de maç günü bu süreyi aşmaz (tahmin yeni eksik listesiyle yeniden üretilir).
 * - SearchPricePer1K / FreeSearchesPerMonth: Google araması maliyeti (AI kullanım raporu için). Gemini 3 modelleri her arama sorgusunu ayrı faturalar; ayın ilk FreeSearchesPerMonth sorgusu ücretsizdir. Token maliyeti ayrıca
 *   Ai:Gemini:InputPricePer1M / OutputPricePer1M ile hesaplanır. Fiyat değişirse yalnızca bu ayarlar güncellenir.
 * - MonthlySearchLimit: Bu ay yapılan Google araması sorgusu bu sayıya ulaşınca arama durur (rapor "Unavailable", tahminler aramasız üretilir), ay başında kendiliğinden açılır. Varsayılan ücretsiz kota kadardır (ücret çıkmaz); 0 = sınırsız. Sayaç ai_usages tablosundan okunur (AI kullanım raporundaki "Google araması" ile aynı sayı).
 */
public sealed class AbsencesOptions
{
    public const string Section = "Absences";

    public bool Enabled { get; set; }
    public string Model { get; set; } = "";
    public int PreliminaryDaysAhead { get; set; } = 30;
    public int PreliminaryCacheHours { get; set; } = 24;
    public int CacheMinutes { get; set; } = 240;
    public decimal SearchPricePer1K { get; set; } = 14m;
    public int FreeSearchesPerMonth { get; set; } = 5000;
    public int MonthlySearchLimit { get; set; } = 5000;
}