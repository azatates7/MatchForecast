namespace MatchForecast.Api.Options;

/*
 * Tahmin üretim ayarları (Forecast bölümü).
 * - PredictionCount: Listelenecek tahmin sayısı.
 * - MinPickProbability / MaxPickProbability: Önce bu banttaki seçenekler seçilir. %85 üstü seçenekler (ör. Çifte Şans 1X)
 *   çok güvenli ama az bilgi verir, %55 altı ise zayıftır. Bantta yeterli seçenek yoksa liste banda en yakın
 *   seçeneklerle tamamlanır. Bandı kapatmak için 0 ve 100 verin.
 * - Max*: Modelden gelen beklenen değerlerin üst sınırları. Gerçekçi maç değerlerinin çok üzerindedir; amaç yalnızca
 *   modelin bariz hatalarını (ör. 25 korner) yakalamak.
 */
public sealed class ForecastOptions
{
    public const string Section = "Forecast";

    public int PredictionCount { get; set; } = 10;
    public int MinPickProbability { get; set; } = 55;
    public int MaxPickProbability { get; set; } = 92;

    public double MaxTeamGoals { get; set; } = 5;
    public double MaxFirstHalfGoals { get; set; } = 4;
    public double MaxCorners { get; set; } = 20;
    public double MaxYellowCards { get; set; } = 10;
    public double MaxRedCards { get; set; } = 1.5;

    // Girişte günün Süper Lig tahminlerini arka planda önceden üret (ForecastPrewarmWorker). false = kapalı.
    public bool PrewarmOnLogin { get; set; } = true;
}