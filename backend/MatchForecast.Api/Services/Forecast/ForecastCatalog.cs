using System.Globalization;
using MatchForecast.Models.Common;

namespace MatchForecast.Api.Services.Forecast;

/*
 * Tahmin listesine girebilecek bir seçenek.
 * - Id: "{marketId}.{sıra}" (eski optionId biçimiyle aynı).
 * - Group: Aynı gruptan en fazla bir seçenek seçilir (ör. 1.5 Üst ve 2.5 Üst birlikte seçilmez).
 * - ReasonFrom: Tahminin gerekçesi bu metriklerin model gerekçelerinden oluşur.
 * - Probability: Projeksiyondan olasılığı hesaplar; gerekli metrik yoksa null döner ve seçenek aday olmaz.
 */
public sealed record CatalogOption(
    string Id,
    int MarketId,
    string Market,
    string Selection,
    string Label,
    string Group,
    StatMetric[] ReasonFrom,
    Func<MatchProjection, double?> Probability);

/*
 * Eski StandardMarkets listesinin yerini alır. Model artık bu listeyi görmez; listedeki her seçeneğin olasılığı
 * MatchProjection'dan hesaplanır. Yeni market eklemek için buraya bir satır eklemek yeterlidir.
 * Eski API-Football market id'leri (1, 12, 5, 8, 13, 6) korunur; yeni marketler 100 ve üstünü kullanır.
 * Korner (yalnızca sayı olarak gösterilir) ve kırmızı kart ("Yok" neredeyse her maçta %80 üstü çıkar) bilerek listede yoktur.
 */
public static class ForecastCatalog
{
    public static readonly IReadOnlyList<CatalogOption> Options = Build();

    private static List<CatalogOption> Build()
    {
        var list = new List<CatalogOption>();
        StatMetric[] goals = [StatMetric.HomeGoals, StatMetric.AwayGoals];

        void Add(int marketId, string market, string group, StatMetric[] reasonFrom,
            params (string Selection, string Label, Func<MatchProjection, double?> Probability)[] options)
        {
            for (var i = 0; i < options.Length; i++)
            {
                var o = options[i];
                list.Add(new CatalogOption($"{marketId}.{i + 1}", marketId, market, o.Selection, o.Label, group, reasonFrom, o.Probability));
            }
        }

        // Her çizgi için önce Üst, sonra Alt seçeneği eklenir.
        void AddLines(int marketId, string market, StatMetric metric, string labelPrefix, params decimal[] lines)
        {
            var options = lines.SelectMany(line =>
            {
                // Türkçe kültürde "1,5" yazılmasın diye kültürden bağımsız biçim.
                var text = line.ToString(CultureInfo.InvariantCulture);
                return new (string, string, Func<MatchProjection, double?>)[]
                {
                    ($"Over {text}", $"{labelPrefix}{text} Üst", p => p.Lambdas.TryGetValue(metric, out var l) ? PoissonMath.Over(l, line) : null),
                    ($"Under {text}", $"{labelPrefix}{text} Alt", p => p.Lambdas.TryGetValue(metric, out var l) ? 1 - PoissonMath.Over(l, line) : null),
                };
            }).ToArray();
            Add(marketId, market, metric.ToString(), [metric], options);
        }

        Add(1, "Match Winner", "MatchWinner", goals,
            ("Home", "MS 1", p => p.Result.Home),
            ("Draw", "MS X", p => p.Result.Draw),
            ("Away", "MS 2", p => p.Result.Away));

        Add(12, "Double Chance", "DoubleChance", goals,
            ("Home/Draw", "ÇŞ 1X", p => p.Result.Home + p.Result.Draw),
            ("Home/Away", "ÇŞ 12", p => p.Result.Home + p.Result.Away),
            ("Draw/Away", "ÇŞ X2", p => p.Result.Draw + p.Result.Away));

        AddLines(5, "Goals Over/Under", StatMetric.TotalGoals, "", 1.5m, 2.5m, 3.5m);

        Add(8, "Both Teams Score", "BothTeamsScore", goals,
            ("Yes", "KG Var", p => p.BothTeamsScore),
            ("No", "KG Yok", p => 1 - p.BothTeamsScore));

        Add(13, "First Half Winner", "FirstHalfWinner", [StatMetric.FirstHalfGoals],
            ("Home", "İY 1", p => p.FirstHalf?.Home),
            ("Draw", "İY X", p => p.FirstHalf?.Draw),
            ("Away", "İY 2", p => p.FirstHalf?.Away));

        AddLines(6, "Goals Over/Under First Half", StatMetric.FirstHalfGoals, "İY ", 0.5m, 1.5m);
        AddLines(103, "Home Team Goals", StatMetric.HomeGoals, "Ev ", 0.5m, 1.5m);
        AddLines(104, "Away Team Goals", StatMetric.AwayGoals, "Dep ", 0.5m, 1.5m);
        AddLines(102, "Yellow Cards Over/Under", StatMetric.YellowCards, "Sarı kart ", 3.5m, 4.5m, 5.5m);

        return list;
    }
}