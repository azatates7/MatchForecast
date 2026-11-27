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
        - Kullanıcı mesajındaki eksik oyuncu listesini hesaba kat. "cezalı", "kadroda yok" ve "sakat" oyuncular bu maçta
          oynamaz; "şüpheli" oyuncular oynayabilir, etkilerini daha az say. Hücum oyuncusu eksikse o takımın, kaleci veya
          savunma oyuncusu eksikse rakibin gol beklentisini buna göre ayarla. Belirgin etkisi olan bir eksik varsa
          ilgili reasoning'de ve summary'de kısaca an.
        - Listede olmayan hiçbir oyuncunun eksik olduğunu varsayma ve isim uydurma. Eksikler henüz netleşmediyse veya
          bilgi alınamadıysa bu konuda yorum yapma.
        - Kullanıcı mesajında hakem satırı varsa yellowCards ve redCards beklentisini hakemin maç başı ortalamalarına ve
          tarzına göre ayarla (lig ve takım etkisini de koruyarak); kart reasoning'inde hakemi kısaca an.
          Hakem satırı yoksa hakem hakkında hiçbir varsayım yapma ve hakemden söz etme.
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

    /// <param name="absences">Eksik oyuncu raporu; null ise bilgi alınamamış sayılır.</param>
    public static string BuildUser(MatchSummary match, AbsenceReport? absences = null)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var sb = new StringBuilder();
        sb.AppendLine($"Maç: {match.HomeTeam} (ev) - {match.AwayTeam} (deplasman)");
        sb.AppendLine($"Lig: {match.League} ({match.Country})");
        sb.AppendLine($"Başlama: {match.Kickoff.ToString("dd MMMM yyyy HH:mm", tr)}");
        sb.AppendLine();
        AppendReferee(sb, absences?.Referee);
        AppendAbsences(sb, match, absences);
        sb.AppendLine();
        sb.AppendLine("Bu maç için metriklerin beklenen değerlerini JSON olarak döndür.");
        return sb.ToString();
    }

    /*
     * Yalnızca hakemin istatistiği veya tarzı bulunduysa yazılır; bilinmeyen alanlar atlanır. Yalnızca ad biliniyorsa
     * yazılmaz: model adı görünce kendi (güncel olmayabilecek) bilgisiyle hakem hakkında varsayım yapmasın. Ör:
     * "Hakem: Halil Umut Meler — maç başına 4,8 sarı, 0,2 kırmızı, 0,3 penaltı (12 maç). Tarz: Sert müdahaleye hızlı kart gösterir."
     */
    private static void AppendReferee(StringBuilder sb, RefereeInfo? r)
    {
        if (r is null || (r.YellowPerMatch is null && r.RedPerMatch is null && r.PenaltiesPerMatch is null
                          && r.FoulsPerMatch is null && string.IsNullOrWhiteSpace(r.Style?.Tr)))
            return;

        var tr = CultureInfo.GetCultureInfo("tr-TR");
        string N(double v) => v.ToString("0.0#", tr);
        var stats = new List<string>();
        if (r.YellowPerMatch is { } y) stats.Add($"{N(y)} sarı");
        if (r.RedPerMatch is { } red) stats.Add($"{N(red)} kırmızı");
        if (r.PenaltiesPerMatch is { } p) stats.Add($"{N(p)} penaltı");
        if (r.FoulsPerMatch is { } f) stats.Add($"{N(f)} faul");

        var line = new StringBuilder($"Hakem: {r.Name}");
        if (stats.Count > 0)
            line.Append($" — maç başına {string.Join(", ", stats)}{(r.Matches is { } m ? $" ({m} maç)" : "")}");
        line.Append('.');
        if (!string.IsNullOrWhiteSpace(r.Style?.Tr))
            line.Append($" Tarz: {r.Style!.Tr.Trim().TrimEnd('.')}.");
        sb.AppendLine(line.ToString());
        sb.AppendLine();
    }

    private static void AppendAbsences(StringBuilder sb, MatchSummary match, AbsenceReport? absences)
    {
        switch (absences?.State)
        {
            case AbsenceReport.Pending:
                sb.AppendLine("Eksik oyuncular: henüz netleşmedi (maç günü açıklanır).");
                return;
            case AbsenceReport.Ready:
                break;
            default:
                sb.AppendLine("Eksik oyuncular: bilgi alınamadı.");
                return;
        }

        sb.AppendLine("Eksik oyuncular (maç günü resmi açıklamalar):");
        foreach (var team in new[] { match.HomeTeam, match.AwayTeam })
        {
            var players = absences!.Players.Where(p => p.Team == team).Select(Describe).ToList();
            sb.AppendLine($"- {team}: {(players.Count == 0 ? "açıklanan eksik yok" : string.Join("; ", players))}");
        }
    }

    // "M. Icardi (sakat, Diz sakatlığı)"
    private static string Describe(TeamAbsence p)
    {
        var status = p.Status switch
        {
            TeamAbsence.Suspended => "cezalı",
            TeamAbsence.NotInSquad => "kadroda yok",
            TeamAbsence.Injured => "sakat",
            _ => "şüpheli"
        };
        return string.IsNullOrWhiteSpace(p.Reason.Tr) ? $"{p.Player} ({status})" : $"{p.Player} ({status}, {p.Reason.Tr})";
    }
}