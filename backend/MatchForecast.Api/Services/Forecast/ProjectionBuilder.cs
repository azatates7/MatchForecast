using MatchForecast.Api.Options;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.Forecast;

// Modelin döndürdüğü JSON; olasılık içermez, yalnızca beklenen değerler ve gerekçeler.
public sealed record AiStatsResponse(
    string? Summary,
    string? SummaryEn,
    AiMetric? HomeGoals,
    AiMetric? AwayGoals,
    AiMetric? FirstHalfGoals,
    AiMetric? Corners,
    AiMetric? YellowCards,
    AiMetric? RedCards);

// Reasoning: Türkçe, ReasoningEn: İngilizce gerekçe (ikisi tek çağrıda istenir).
public sealed record AiMetric(double? Expected, string? Reasoning, string? ReasoningEn = null);

/*
 * Doğrulanmış λ'lar ve bunlardan türetilen tüm olasılıklar. Result/BothTeamsScore/FirstHalf yuvarlanmamış değerlerdir; tahmin seçimi bunlarla yapılır, ekranda gösterilen yüzdeler Outcomes ve Stats içindedir.
 * Distributions: her metriğin dağılımı (üst/alt ve aralık bunlardan hesaplanır): ev/deplasman gol → Poisson, toplam ve ilk yarı gol → Dixon-Coles skor tablosu, korner/sarı kart → negatif binom.
 */
public sealed record MatchProjection(
    IReadOnlyDictionary<StatMetric, double> Lambdas,
    IReadOnlyDictionary<StatMetric, LocalizedText> Reasons,
    (double Home, double Draw, double Away) Result,
    double BothTeamsScore,
    (double Home, double Draw, double Away)? FirstHalf,
    OutcomeProbabilities Outcomes,
    IReadOnlyList<StatProjection> Stats,
    IReadOnlyDictionary<StatMetric, CountDistribution> Distributions)
{
    public double? Over(StatMetric metric, decimal line) => Distributions.TryGetValue(metric, out var d) ? d.Over(line) : null;
}

/*
 * Model çıktısını doğrular ve olasılıklara çevirir.
 * - Ev veya deplasman gol beklentisi yoksa tahmin üretilemez (1X2, KG ve toplam gol bunlara bağlı) -> ForecastException.
 * - Diğer metriklerden biri eksik/geçersizse yalnızca o metrik atlanır; varsayılan değer uydurulmaz.
 * - Negatif değerler 0'a, aşırı değerler ForecastOptions'taki üst sınırlara çekilir; ilk yarı golü toplamı geçemez.
 */
public static class ProjectionBuilder
{
    private sealed record MetricDef(StatMetric Metric, LocalizedText Label, decimal[] Lines);

    // Sıra, ekrandaki istatistik tablosunun sırasıdır. Korner için çizgi yoktur: yalnızca beklenen sayı ve aralık gösterilir.
    private static readonly MetricDef[] Defs =
    [
        new(StatMetric.HomeGoals, new("Ev sahibi gol", "Home goals"), [0.5m, 1.5m]),
        new(StatMetric.AwayGoals, new("Deplasman gol", "Away goals"), [0.5m, 1.5m]),
        new(StatMetric.TotalGoals, new("Toplam gol", "Total goals"), [1.5m, 2.5m, 3.5m]),
        new(StatMetric.FirstHalfGoals, new("İlk yarı gol", "First-half goals"), [0.5m, 1.5m]),
        new(StatMetric.Corners, new("Korner", "Corners"), []),
        new(StatMetric.YellowCards, new("Sarı kart", "Yellow cards"), [3.5m, 4.5m, 5.5m]),
        new(StatMetric.RedCards, new("Kırmızı kart", "Red cards"), [0.5m]),
    ];

    public static MatchProjection Build(AiStatsResponse ai, ForecastOptions opt, ILogger logger)
    {
        var lambdas = new Dictionary<StatMetric, double>();
        var reasons = new Dictionary<StatMetric, LocalizedText>();

        void Take(StatMetric metric, AiMetric? value, double max)
        {
            if (value?.Expected is not double expected || double.IsNaN(expected) || double.IsInfinity(expected))
            {
                logger.LogWarning("AI response has no valid expected value for {Metric}; metric skipped.", metric);
                return;
            }
            lambdas[metric] = Math.Round(Math.Clamp(expected, 0, max), 2);
            reasons[metric] = LocalizedText.Of(value.Reasoning, value.ReasoningEn);
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
            throw new ForecastException(Lang.T("Yapay zeka cevabı okunamadı, tekrar deneyin.", "The AI response could not be read, please try again."));
        }

        var total = Math.Round(home + away, 2);
        lambdas[StatMetric.TotalGoals] = total;
        reasons[StatMetric.TotalGoals] = Join(reasons[StatMetric.HomeGoals], reasons[StatMetric.AwayGoals]);

        var rho = Math.Clamp(opt.DixonColesRho, -0.2, 0);
        var scores = new ScoreMatrix(home, away, rho);
        var result = scores.Outcome();
        var btts = scores.BothTeamsScore();

        var distributions = new Dictionary<StatMetric, CountDistribution>
        {
            [StatMetric.HomeGoals] = CountDistribution.Poisson(home),
            [StatMetric.AwayGoals] = CountDistribution.Poisson(away),
            [StatMetric.TotalGoals] = scores.Total()
        };

        (double Home, double Draw, double Away)? firstHalf = null;
        if (lambdas.TryGetValue(StatMetric.FirstHalfGoals, out var fh))
        {
            fh = Math.Min(fh, total);
            lambdas[StatMetric.FirstHalfGoals] = fh;
            // İlk yarı golü takımlara tam maçtaki gol beklentisi oranında bölünür.
            var homeShare = total > 0 ? home / total : 0.5;
            var firstHalfScores = new ScoreMatrix(fh * homeShare, fh * (1 - homeShare), rho);
            firstHalf = firstHalfScores.Outcome();
            distributions[StatMetric.FirstHalfGoals] = firstHalfScores.Total();
        }

        if (lambdas.TryGetValue(StatMetric.Corners, out var corners))
            distributions[StatMetric.Corners] = CountDistribution.NegativeBinomial(corners, opt.CornersDispersion);
        if (lambdas.TryGetValue(StatMetric.YellowCards, out var yellow))
            distributions[StatMetric.YellowCards] = CountDistribution.NegativeBinomial(yellow, opt.YellowCardsDispersion);
        if (lambdas.TryGetValue(StatMetric.RedCards, out var red))
            distributions[StatMetric.RedCards] = CountDistribution.Poisson(red);

        var (h, d, a) = PoissonMath.ToPercents(result);
        var fhPercents = firstHalf is { } f ? PoissonMath.ToPercents(f) : ((int, int, int)?)null;
        var outcomes = new OutcomeProbabilities(
            h, d, a,
            PoissonMath.ToPercent(btts),
            fhPercents?.Item1, fhPercents?.Item2, fhPercents?.Item3);

        var stats = Defs
            .Where(def => lambdas.ContainsKey(def.Metric))
            .Select(def => ToStat(def, lambdas[def.Metric], distributions[def.Metric], reasons[def.Metric]))
            .ToList();

        return new MatchProjection(lambdas, reasons, result, btts, firstHalf, outcomes, stats, distributions);
    }

    private static StatProjection ToStat(MetricDef def, double lambda, CountDistribution dist, LocalizedText reasoning)
    {
        var lines = def.Lines
            .Select(line =>
            {
                var over = PoissonMath.ToPercent(dist.Over(line));
                return new LineProbability(line, over, 100 - over);
            })
            .ToList();

        return new StatProjection(
            def.Metric,
            def.Label,
            (decimal)Math.Round(lambda, 2),
            dist.Quantile(0.10),
            dist.Quantile(0.90),
            lines,
            reasoning);
    }

    internal static string Join(params string[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct());

    // Gerekçeleri her dil için ayrı birleştirir.
    internal static LocalizedText Join(params LocalizedText[] parts) =>
        new(Join(parts.Select(p => p.Tr).ToArray()), Join(parts.Select(p => p.En).ToArray()));
}