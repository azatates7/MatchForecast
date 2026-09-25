using System.Globalization;
using System.Text;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services;

/// <summary>
/// Prompt metinleri tek yerde. Her seçeneğe kısa bir kimlik (ör. "5.3") verilir;
/// model sadece bu kimliği döner, böylece listede olmayan bir bahis uyduramaz.
/// </summary>
public static class ForecastPrompt
{
    public const int PredictionCount = 5;
    private const int MaxOptions = 400;

    public const string System = """
        Sen deneyimli bir futbol analistisin. Görevin, verilen maçın bahis marketleri arasından
        gerçekleşme olasılığı EN YÜKSEK olan seçenekleri bulmak.

        Kurallar:
        - Yalnızca listede verilen seçenek kimliklerini (optionId) kullan; yeni bahis uydurma.
        - Tam olarak 5 farklı seçenek seç. Aynı sonucun neredeyse aynısı olan seçenekleri
          (ör. "Üst 0.5" ve "Üst 1.5") birlikte seçmekten kaçın; çeşitlilik sağla.
        - Ölçüt değer (value bet) değil, gerçekleşme olasılığıdır. Oranlardan türetilen ima edilen
          olasılığı, takımların güncel gücü, lig karakteri ve ev sahibi avantajı hakkındaki bilginle birleştir.
        - confidence: 0-100 arası, senin tahmini gerçekleşme olasılığın.
        - label: seçeneğin kısa Türkçe karşılığı (ör. "İY 0.5 Alt — ilk yarı gol olmaz", "MS 2", "KG Var", "2.5 Üst").
        - reasoning: en fazla 2 cümle, Türkçe.
        - Cevabın SADECE geçerli JSON olsun; açıklama, markdown veya kod bloğu ekleme.

        JSON şeması:
        {
          "summary": "maçın 1-2 cümlelik genel değerlendirmesi",
          "predictions": [
            { "optionId": "5.3", "label": "...", "confidence": 78, "reasoning": "..." }
          ]
        }
        """;

    public static string BuildUser(MatchSummary match, MatchOdds odds, out Dictionary<string, (Market Market, OddOption Option)> index)
    {
        index = new Dictionary<string, (Market, OddOption)>();
        var sb = new StringBuilder();
        var tr = CultureInfo.GetCultureInfo("tr-TR");

        sb.AppendLine($"Maç: {match.HomeTeam} (ev) - {match.AwayTeam} (deplasman)");
        sb.AppendLine($"Lig: {match.League} ({match.Country})");
        sb.AppendLine($"Başlama: {match.Kickoff.ToString("dd MMMM yyyy HH:mm", tr)}");
        sb.AppendLine($"Bahisçi: {odds.Bookmaker}");
        sb.AppendLine();
        sb.AppendLine("Seçenekler (optionId | market | seçenek | oran | ima edilen olasılık):");

        var count = 0;
        foreach (var market in odds.Markets)
        {
            for (var i = 0; i < market.Options.Count && count < MaxOptions; i++, count++)
            {
                var o = market.Options[i];
                var id = $"{market.Id}.{i + 1}";
                index[id] = (market, o);
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{id} | {market.Name} | {o.Value} | {o.Odd:0.00} | %{ImpliedProbability(o.Odd):0.0}"));
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Gerçekleşme olasılığı en yüksek {PredictionCount} seçeneği JSON olarak döndür.");
        return sb.ToString();
    }

    public static decimal ImpliedProbability(decimal odd) => odd <= 0 ? 0 : Math.Round(100m / odd, 1);
}
