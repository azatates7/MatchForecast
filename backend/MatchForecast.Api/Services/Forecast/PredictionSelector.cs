using MatchForecast.Api.Options;
using MatchForecast.Models.Common;

namespace MatchForecast.Api.Services.Forecast;

/*
 * Katalogdaki seçeneklerden tahmin listesini seçer. Eski sistem prompt'undaki "çeşitlilik sağla" kuralının kodla
 * yapılan karşılığıdır; sonuç deterministiktir (aynı projeksiyon her zaman aynı listeyi verir).
 * 1. Olasılığı hesaplanabilen tüm seçenekler yüzdeye çevrilir.
 * 2. Önce olasılık bandındaki (varsayılan %55-%85) seçenekler, yüksek olasılıktan düşüğe doğru alınır.
 * 3. Yetmezse banda en yakın olanlarla tamamlanır.
 * 4. Her gruptan en fazla bir seçenek alınır.
 * 5. Sonuç olasılığa göre azalan sıralanır.
 */
public static class PredictionSelector
{
    public static IReadOnlyList<Prediction> Select(MatchProjection projection, ForecastOptions opt)
    {
        var candidates = ForecastCatalog.Options
            .Select((option, index) => (Option: option, Index: index, P: option.Probability(projection)))
            .Where(c => c.P is not null)
            .Select(c => (c.Option, c.Index, Percent: PoissonMath.ToPercent(c.P!.Value)))
            .OrderByDescending(c => c.Percent)
            .ThenBy(c => c.Index)
            .ToList();

        bool InBand(int p) => p >= opt.MinPickProbability && p <= opt.MaxPickProbability;
        int DistanceToBand(int p) => p < opt.MinPickProbability ? opt.MinPickProbability - p
            : p > opt.MaxPickProbability ? p - opt.MaxPickProbability : 0;

        var ordered = candidates.Where(c => InBand(c.Percent))
            .Concat(candidates.Where(c => !InBand(c.Percent)).OrderBy(c => DistanceToBand(c.Percent)));

        var chosen = new List<(CatalogOption Option, int Percent)>();
        var usedGroups = new HashSet<string>();
        foreach (var c in ordered)
        {
            if (chosen.Count == opt.PredictionCount)
                break;
            if (usedGroups.Add(c.Option.Group))
                chosen.Add((c.Option, c.Percent));
        }

        return chosen
            .OrderByDescending(c => c.Percent)
            .Select((c, i) => new Prediction(
                i + 1,
                c.Option.Label,
                c.Option.Market,
                c.Option.Selection,
                c.Percent,
                ProjectionBuilder.Join(c.Option.ReasonFrom
                    .Select(m => projection.Reasons.TryGetValue(m, out var r) ? r : "")
                    .ToArray())))
            .ToList();
    }
}