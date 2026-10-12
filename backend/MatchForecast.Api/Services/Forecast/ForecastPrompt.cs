using System.Globalization;
using System.Text;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.Forecast;

/*
 * Prompt metinleri tek yerde. Model artık bahis seçeneği seçmez; yalnızca metriklerin beklenen değerlerini (λ) verir.
 * Olasılıklar ve tahmin listesi kodda hesaplanır (ProjectionBuilder, PredictionSelector). Bu yüzden prompt'ta seçenek
 * listesi yoktur ve boyutu oranlı eski prompt'un küçük bir kesridir.
 */
public static class ForecastPrompt
{
    public const string System = """
        Sen deneyimli bir futbol analistisin. Görevin, verilen maç için aşağıdaki istatistiklerin
        BEKLENEN değerlerini tahmin etmek.

        Metrikler (uzatmalar hariç, normal süre):
        - homeGoals: ev sahibinin atacağı gol
        - awayGoals: deplasman takımının atacağı gol
        - firstHalfGoals: ilk yarıda atılacak toplam gol (iki takım)
        - corners: maçtaki toplam korner (iki takım)
        - yellowCards: maçtaki toplam sarı kart (iki takım)
        - redCards: maçtaki toplam kırmızı kart (iki takım)

        Kurallar:
        - expected bir ortalama beklentidir, skor tahmini değildir; ondalıklı yaz (ör. 1.4).
        - Başlangıç noktası olarak üst düzey liglerin yaklaşık maç ortalamalarını al (toplam gol ~2.7,
          ilk yarı gol ~1.2, korner ~10, sarı kart ~4.5, kırmızı kart ~0.2) ve bunu takımların gücü,
          oyun tarzı, lig karakteri ve ev sahibi avantajına göre ayarla.
        - reasoning: en fazla 1 cümle, Türkçe.
        - summary: maçın 1-2 cümlelik genel değerlendirmesi, Türkçe.
        - Cevabın SADECE geçerli JSON olsun; açıklama, markdown veya kod bloğu ekleme.

        JSON şeması:
        {
          "summary": "...",
          "homeGoals": { "expected": 1.5, "reasoning": "..." },
          "awayGoals": { "expected": 1.1, "reasoning": "..." },
          "firstHalfGoals": { "expected": 1.2, "reasoning": "..." },
          "corners": { "expected": 10.0, "reasoning": "..." },
          "yellowCards": { "expected": 4.5, "reasoning": "..." },
          "redCards": { "expected": 0.2, "reasoning": "..." }
        }
        """;

    public static string BuildUser(MatchSummary match)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var sb = new StringBuilder();
        sb.AppendLine($"Maç: {match.HomeTeam} (ev) - {match.AwayTeam} (deplasman)");
        sb.AppendLine($"Lig: {match.League} ({match.Country})");
        sb.AppendLine($"Başlama: {match.Kickoff.ToString("dd MMMM yyyy HH:mm", tr)}");
        sb.AppendLine();
        sb.AppendLine("Bu maç için metriklerin beklenen değerlerini JSON olarak döndür.");
        return sb.ToString();
    }
}