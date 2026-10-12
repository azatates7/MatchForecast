using MatchForecast.Api.Options;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.Forecast;

// Modelin döndürdüğü JSON; olasılık içermez, yalnızca beklenen değerler ve gerekçeler.
public sealed record AiStatsResponse(
    string? Summary,
    AiMetric? HomeGoals,
    AiMetric? AwayGoals,
    AiMetric? FirstHalfGoals,
    AiMetric? Corners,
    AiMetric? YellowCards,
    AiMetric? RedCards);

public sealed record AiMetric(double? Expected, string? Reasoning);

/*
 * Doğrulanmış λ'lar ve bunlardan türetilen tüm olasılıklar. Result/BothTeamsScore/FirstHalf yuvarlanmamış değerlerdir;
 * tahmin seçimi bunlarla yapılır, ekranda gösterilen yüzdeler Outcomes ve Stats içindedir.
 */
public sealed record MatchProjection(
    IReadOnlyDictionary<StatMetric, double> Lambdas,
    IReadOnlyDictionary<StatMetric, string> Reasons,
    (double Home, double Draw, double Away) Result,
    double BothTeamsScore,
    (double Home, double Draw, double Away)? FirstHalf,
    OutcomeProbabilities Outcomes,
    IReadOnlyList<StatProjection> Stats);

/*
 * Model çıktısını doğrular ve olasılıklara çevirir.
 * - Ev veya deplasman gol beklentisi yoksa tahmin üretilemez (1X2, KG ve toplam gol bunlara bağlı) -> ForecastException.
 * - Diğer metriklerden biri eksik/geçersizse yalnızca o metrik atlanır; varsayılan değer uydurulmaz.
 * - Negatif değerler 0'a, aşırı değerler ForecastOptions'taki üst sınırlara çekilir; ilk yarı golü toplamı geçemez.
 */
public static class ProjectionBuilder
{
    private sealed record MetricDef(StatMetric Metric, string Label, decimal[] Lines);

    // Sıra, ekrandaki istatistik tablosunun sırasıdır. Korner için çizgi yoktur: yalnızca beklenen sayı ve aralık gösterilir.
    private static readonly MetricDef[] Defs =
    [
        new(StatMetric.HomeGoals, "Ev sahibi gol", [0.5m, 1.5m]),
        new(StatMetric.AwayGoals, "Deplasman gol", [0.5m, 1.5m]),
        new(StatMetric.TotalGoals, "Toplam gol", [1.5m, 2.5m, 3.5m]),
        new(StatMetric.FirstHalfGoals, "İlk yarı gol", [0.5m, 1.5m]),
        new(StatMetric.Corners, "Korner", []),
        new(StatMetric.YellowCards, "Sarı kart", [3.5m, 4.5m, 5.5m]),
        new(StatMetric.RedCards, "Kırmızı kart", [0.5m]),
    ];

    public static MatchProjection Build(AiStatsResponse ai, ForecastOptions opt, ILogger logger)
    {
        var lambdas = new Dictionary<StatMetric, double>();
        var reasons = new Dictionary<StatMetric, string>();

        void Take(StatMetric metric, AiMetric? value, double max)
        {
            if (value?.Expected is not double expected || double.IsNaN(expected) || double.IsInfinity(expected))
            {
                logger.LogWarning("AI response has no valid expected value for {Metric}; metric skipped.", metric);
                return;
            }
            lambdas[metric] = Math.Round(Math.Clamp(expected, 0, max), 2);
            reasons[metric] = value.Reasoning?.Trim() ?? "";
        }

        Take(StatMetric.HomeGoals, ai.HomeGoals, opt.MaxTeamGoals);
        Take(StatMetric.AwayGoals, ai.AwayGoals, opt.MaxTeamGoals);
        Take(StatMetric.FirstHalfGoals, ai.FirstHalfGoals, opt.MaxFirstHalfGoals);
        Take(StatMetric.Corners, ai.Corners, opt.MaxCorners);
        Take(StatMetric.YellowCards, ai.YellowCards, opt.MaxYellowCards);
        Take(StatMetric.RedCards, ai.RedCards, opt.MaxRedCards);

        if (!lambdas.TryGetValue(StatMetric.HomeGoals, out var home) || !lambdas.TryGetValue(StatMetric.AwayGoals, out var away))
        {
            logger.LogWarning("AI response is missing home/away goal expectations; forecast cannot be built.");
            throw new ForecastException("Yapay zeka cevabı okunamadı, tekrar deneyin.");
        }

        var total = Math.Round(home + away, 2);
        lambdas[StatMetric.TotalGoals] = total;
        reasons[StatMetric.TotalGoals] = Join(reasons[StatMetric.HomeGoals], reasons[StatMetric.AwayGoals]);

        var result = PoissonMath.Outcome(home, away);
        var btts = PoissonMath.BothTeamsScore(home, away);

        (double Home, double Draw, double Away)? firstHalf = null;
        if (lambdas.TryGetValue(StatMetric.FirstHalfGoals, out var fh))
        {
            fh = Math.Min(fh, total);
            lambdas[StatMetric.FirstHalfGoals] = fh;
            // İlk yarı golü takımlara tam maçtaki gol beklentisi oranında bölünür.
            var homeShare = total > 0 ? home / total : 0.5;
            firstHalf = PoissonMath.Outcome(fh * homeShare, fh * (1 - homeShare));
        }

        var (h, d, a) = PoissonMath.ToPercents(result);
        var fhPercents = firstHalf is { } f ? PoissonMath.ToPercents(f) : ((int, int, int)?)null;
        var outcomes = new OutcomeProbabilities(
            h, d, a,
            PoissonMath.ToPercent(btts),
            fhPercents?.Item1, fhPercents?.Item2, fhPercents?.Item3);

        var stats = Defs
            .Where(def => lambdas.ContainsKey(def.Metric))
            .Select(def => ToStat(def, lambdas[def.Metric], reasons[def.Metric]))
            .ToList();

        return new MatchProjection(lambdas, reasons, result, btts, firstHalf, outcomes, stats);
    }

    private static StatProjection ToStat(MetricDef def, double lambda, string reasoning)
    {
        var lines = def.Lines
            .Select(line =>
            {
                var over = PoissonMath.ToPercent(PoissonMath.Over(lambda, line));
                return new LineProbability(line, over, 100 - over);
            })
            .ToList();

        return new StatProjection(
            def.Metric,
            def.Label,
            (decimal)Math.Round(lambda, 2),
            PoissonMath.Quantile(lambda, 0.10),
            PoissonMath.Quantile(lambda, 0.90),
            lines,
            reasoning);
    }

    internal static string Join(params string[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct());
}