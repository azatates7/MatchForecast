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
        - Kullanıcı mesajındaki eksik oyuncu listesini (sakat, cezalı vb.) hesaba kat: hücum oyuncusu eksikse o takımın,
          kaleci veya savunma oyuncusu eksikse rakibin gol beklentisini buna göre ayarla. "Şüpheli" oyuncular oynayabilir;
          etkilerini daha az say. Belirgin etkisi olan bir eksik varsa ilgili reasoning'de kısaca an.
        - Listede olmayan hiçbir oyuncunun sakat veya cezalı olduğunu varsayma ve isim uydurma. Eksik oyuncu bilgisi
          alınamadıysa bu konuda yorum yapma.
        - reasoning: en fazla 1 cümle, Türkçe. reasoningEn: aynı cümlenin İngilizcesi.
        - summary: maçın 1-2 cümlelik genel değerlendirmesi, Türkçe. summaryEn: aynı metnin İngilizcesi.
        - Cevabın SADECE geçerli JSON olsun; açıklama, markdown veya kod bloğu ekleme.

        JSON şeması:
        {
          "summary": "...",
          "summaryEn": "...",
          "homeGoals": { "expected": 1.5, "reasoning": "...", "reasoningEn": "..." },
          "awayGoals": { "expected": 1.1, "reasoning": "...", "reasoningEn": "..." },
          "firstHalfGoals": { "expected": 1.2, "reasoning": "...", "reasoningEn": "..." },
          "corners": { "expected": 10.0, "reasoning": "...", "reasoningEn": "..." },
          "yellowCards": { "expected": 4.5, "reasoning": "...", "reasoningEn": "..." },
          "redCards": { "expected": 0.2, "reasoning": "...", "reasoningEn": "..." }
        }
        """;

    // Takım başına en fazla bu kadar oyuncu yazılır; uzun listeler prompt'u şişirmesin.
    private const int MaxMissingPerTeam = 12;

    /// <param name="missing">Eksik oyuncular; null ise veri alınamamıştır (boş liste: bildirilen eksik yok).</param>
    public static string BuildUser(MatchSummary match, IReadOnlyList<MissingPlayer>? missing = null)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var sb = new StringBuilder();
        sb.AppendLine($"Maç: {match.HomeTeam} (ev) - {match.AwayTeam} (deplasman)");
        sb.AppendLine($"Lig: {match.League} ({match.Country})");
        sb.AppendLine($"Başlama: {match.Kickoff.ToString("dd MMMM yyyy HH:mm", tr)}");
        sb.AppendLine();
        AppendMissingPlayers(sb, match, missing);
        sb.AppendLine();
        sb.AppendLine("Bu maç için metriklerin beklenen değerlerini JSON olarak döndür.");
        return sb.ToString();
    }

    private static void AppendMissingPlayers(StringBuilder sb, MatchSummary match, IReadOnlyList<MissingPlayer>? missing)
    {
        if (missing is null)
        {
            sb.AppendLine("Eksik oyuncular: bilgi alınamadı.");
            return;
        }

        sb.AppendLine("Eksik oyuncular (sakatlık, ceza vb.; kaynak: API-Football):");
        foreach (var team in new[] { match.HomeTeam, match.AwayTeam })
        {
            var players = missing
                .Where(p => string.Equals(p.Team, team, StringComparison.OrdinalIgnoreCase))
                .Take(MaxMissingPerTeam)
                .Select(Describe)
                .ToList();
            sb.AppendLine($"- {team}: {(players.Count == 0 ? "bildirilen eksik yok" : string.Join("; ", players))}");
        }
    }

    // "D. Costa (oynamayacak, Suspended)"; neden İngilizce kalır (sağlayıcı metni).
    private static string Describe(MissingPlayer p)
    {
        var status = p.Type switch
        {
            "Missing Fixture" => "oynamayacak",
            "Questionable" => "şüpheli",
            _ => p.Type
        };
        var detail = string.Join(", ", new[] { status, p.Reason }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return detail.Length == 0 ? p.Player : $"{p.Player} ({detail})";
    }
}