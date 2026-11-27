namespace MatchForecast.Api.Options;

/*
 * Tahmin isabet takibi (Accuracy bölümü). ResultSettlementWorker, biten maçların sonucunu API-Football'dan toplar.
 * - Enabled: Kapalıyken sonuç toplanmaz; rapor yalnızca önceden toplanmış sonuçları gösterir.
 * - IntervalMinutes: Toplama turları arası süre.
 * - MaxRequestsPerRun: Bir turda en fazla kaç API-Football isteği atılır. Her istek 20 maçı birden sorgular
 *   (fixtures?ids=...), 3 istek = 60 maç. Ücretsiz plan günlük 100 istekle sınırlı olduğu için düşük tutulur.
 * - LookbackDays: Başlama saatinden bu kadar gün sonra hâlâ sonucu alınamayan maç için denemekten vazgeçilir.
 */
public sealed class AccuracyOptions
{
    public const string Section = "Accuracy";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public int MaxRequestsPerRun { get; set; } = 3;
    public int LookbackDays { get; set; } = 7;
}