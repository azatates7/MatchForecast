using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatchForecast.Tests;

// ProjectionBuilder (doğrulama) ve PredictionSelector (seçim) birlikte test edilir; ikisi de saf fonksiyondur.
public class PredictionSelectorTests
{
    // Beklenen ilk 5 seçim test ediliyor; varsayılan PredictionCount (10) değişse de testler etkilenmesin.
    private static readonly ForecastOptions DefaultOptions = new() { PredictionCount = 5 };

    private static AiStatsResponse Sample(double? home = 1.6, double? away = 1.0, double? firstHalf = 1.1,
        double? corners = 10.2, double? yellow = 4.6, double? red = 0.2) =>
        new("Dengeli maç.",
            new AiMetric(home, "Ev sahibi içeride etkili."),
            new AiMetric(away, "Deplasman hücumu zayıf."),
            new AiMetric(firstHalf, "İlk yarı temkinli."),
            new AiMetric(corners, "Kanat oyunu."),
            new AiMetric(yellow, "Sert maç."),
            new AiMetric(red, "Lig ortalaması."));

    private static MatchProjection Build(AiStatsResponse ai, ForecastOptions? opt = null) =>
        ProjectionBuilder.Build(ai, opt ?? DefaultOptions, NullLogger.Instance);

    [Fact]
    public void Build_Throws_WhenGoalExpectationMissing()
    {
        Assert.Throws<ForecastException>(() => Build(Sample(home: null)));
    }

    [Fact]
    public void Build_SkipsOptionalMetric_WhenMissing()
    {
        var projection = Build(Sample(corners: null));

        Assert.DoesNotContain(projection.Stats, s => s.Metric == StatMetric.Corners);
        Assert.Contains(projection.Stats, s => s.Metric == StatMetric.TotalGoals);
    }

    [Fact]
    public void Build_ClampsInvalidValues()
    {
        var projection = Build(Sample(away: -1, firstHalf: 3.5, corners: 40));

        Assert.Equal(0, projection.Lambdas[StatMetric.AwayGoals]);
        Assert.Equal(20, projection.Lambdas[StatMetric.Corners]);
        // İlk yarı golü toplam gol beklentisini (1.6 + 0) geçemez.
        Assert.Equal(1.6, projection.Lambdas[StatMetric.FirstHalfGoals]);
    }

    [Fact]
    public void Build_ComputesTotalGoals_AndConsistentPercentages()
    {
        var projection = Build(Sample());

        var total = projection.Stats.Single(s => s.Metric == StatMetric.TotalGoals);
        Assert.Equal(2.6m, total.Expected);
        Assert.Equal(1, total.RangeMin);
        Assert.Equal(5, total.RangeMax);
        Assert.All(total.Lines, l => Assert.Equal(100, l.Over + l.Under));

        var o = projection.Outcomes;
        Assert.Equal(100, o.Home + o.Draw + o.Away);
        Assert.Equal(100, o.FirstHalfHome!.Value + o.FirstHalfDraw!.Value + o.FirstHalfAway!.Value);
    }

    [Fact]
    public void Build_CornersHaveNoLines()
    {
        var corners = Build(Sample()).Stats.Single(s => s.Metric == StatMetric.Corners);

        Assert.Empty(corners.Lines);
        Assert.Equal(10.2m, corners.Expected);
    }

    [Fact]
    public void Select_ReturnsExpectedPicks_ForSampleMatch()
    {
        var picks = PredictionSelector.Select(Build(Sample()), DefaultOptions);

        Assert.Equal(new[] { "Ev 0.5 Üst", "ÇŞ 1X", "3.5 Alt", "Dep 1.5 Alt", "İY 1.5 Alt" }, picks.Select(p => p.Label));
        Assert.Equal(new[] { 80, 76, 74, 74, 70 }, picks.Select(p => p.Confidence));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, picks.Select(p => p.Rank));
    }

    [Fact]
    public void Select_TakesAtMostOnePerGroup()
    {
        var picks = PredictionSelector.Select(Build(Sample()), DefaultOptions);

        var groups = picks.Select(p => ForecastCatalog.Options.Single(o => o.Market == p.Market && o.Selection == p.Selection).Group);
        Assert.Equal(picks.Count, groups.Distinct().Count());
    }

    [Fact]
    public void Select_NeverPicksCornersOrRedCards()
    {
        var picks = PredictionSelector.Select(Build(Sample(corners: 12, red: 0.05)), new ForecastOptions { MinPickProbability = 0, MaxPickProbability = 100 });

        Assert.DoesNotContain(picks, p => p.Market.Contains("Corner") || p.Market.Contains("Red"));
    }

    [Fact]
    public void Select_FillsOutsideBand_WhenBandHasTooFewOptions()
    {
        // %99-%100 bandına hiçbir seçenek girmez; liste yine de banda en yakın seçeneklerle 5'e tamamlanır.
        var picks = PredictionSelector.Select(Build(Sample()), new ForecastOptions { PredictionCount = 10, MinPickProbability = 99, MaxPickProbability = 100 });

        Assert.Equal(5, picks.Count);
        Assert.Equal("Ev 0.5 Üst", picks[0].Label);
    }

    [Fact]
    public void Select_UsesMetricReasoning()
    {
        var picks = PredictionSelector.Select(Build(Sample()), DefaultOptions);

        Assert.Equal("Ev sahibi içeride etkili.", picks.Single(p => p.Label == "Ev 0.5 Üst").Reasoning);
        Assert.Equal("Ev sahibi içeride etkili. Deplasman hücumu zayıf.", picks.Single(p => p.Label == "ÇŞ 1X").Reasoning);
    }
}