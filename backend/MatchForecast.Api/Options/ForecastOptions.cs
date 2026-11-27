namespace MatchForecast.Api.Options;

/*
 * Tahmin üretim ayarları (Forecast bölümü).
 * - PredictionCount: Listelenecek tahmin sayısı.
 * - MinPickProbability / MaxPickProbability: Önce bu banttaki seçenekler seçilir. %85 üstü seçenekler (ör. Çifte Şans 1X)
 *   çok güvenli ama az bilgi verir, %55 altı ise zayıftır. Bantta yeterli seçenek yoksa liste banda en yakın
 *   seçeneklerle tamamlanır. Bandı kapatmak için 0 ve 100 verin.
 * - MinShownProbability: Kesin alt sınır; bu olasılığın altındaki seçenekler (ör. %44) tamamlama aşamasında da hiç
 *   listelenmez. Varsayılan 51: yalnızca %50'nin üzerindekiler. Yeterli seçenek yoksa liste PredictionCount'tan kısa olur.
 * - DixonColesRho: Skor tablosu düzeltmesi (ScoreMatrix). Negatif değer 0-0 ve 1-1'i artırır, 1-0 / 0-1'i azaltır;
 *   bağımsız Poisson'un beraberliği düşük hesaplamasını giderir. Literatürde genelde -0,05 ile -0,15 arası bulunur;
 *   0 = düzeltme yok. -0,2 ile 0 arasında olmalı.
 * - CornersDispersion / YellowCardsDispersion: Negatif binom dağılım parametresi (r). Varyans = λ + λ²/r; küçük r daha
 *   geniş dağılım demektir. 0 = Poisson. Varsayılanlar yaklaşık değerlerdir; isabet raporundaki korner/kart
 *   kalibrasyonuna göre ayarlanabilir (seçenekler gerçekleşenden iddialıysa r düşürülür).
 * - Max*: Modelden gelen beklenen değerlerin üst sınırları. Gerçekçi maç değerlerinin çok üzerindedir; amaç yalnızca
 *   modelin bariz hatalarını (ör. 25 korner) yakalamak.
 */
public sealed class ForecastOptions
{
    public const string Section = "Forecast";

    public int PredictionCount { get; set; } = 10;
    public int MinPickProbability { get; set; } = 55;
    public int MaxPickProbability { get; set; } = 95;
    public int MinShownProbability { get; set; } = 51;

    public double DixonColesRho { get; set; } = -0.10;
    public double CornersDispersion { get; set; } = 40;
    public double YellowCardsDispersion { get; set; } = 20;

    public double MaxTeamGoals { get; set; } = 5;
    public double MaxFirstHalfGoals { get; set; } = 4;
    public double MaxCorners { get; set; } = 20;
    public double MaxYellowCards { get; set; } = 10;
    public double MaxRedCards { get; set; } = 1.5;

    // Girişte günün Süper Lig tahminlerini arka planda önceden üret (ForecastPrewarmWorker). false = kapalı.
    public bool PrewarmOnLogin { get; set; } = true;
}