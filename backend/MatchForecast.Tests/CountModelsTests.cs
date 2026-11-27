using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatchForecast.Tests;

// Dixon-Coles skor tablosu ve negatif binom. Beklenen değerler formüllerden bağımsız olarak (Python) hesaplanmıştır.
public class CountModelsTests
{
    [Fact]
    public void ScoreMatrix_WithoutCorrection_EqualsIndependentPoisson()
    {
        var independent = PoissonMath.Outcome(1.6, 1.0);
        var matrix = new ScoreMatrix(1.6, 1.0, rho: 0).Outcome();

        Assert.Equal(independent.Home, matrix.Home, 10);
        Assert.Equal(independent.Draw, matrix.Draw, 10);
        Assert.Equal(PoissonMath.BothTeamsScore(1.6, 1.0), new ScoreMatrix(1.6, 1.0).BothTeamsScore(), 4);
    }

    [Fact]
    public void ScoreMatrix_DixonColes_RaisesDraw_AndMatchesReference()
    {
        var m = new ScoreMatrix(1.6, 1.0, rho: -0.1);
        var (home, draw, away) = m.Outcome();

        // Python: (0.50155, 0.27377, 0.22468), KG 0.51638, 3.5 Üst 0.26400
        Assert.Equal(0.5016, home, 4);
        Assert.Equal(0.2738, draw, 4);
        Assert.Equal(0.2247, away, 4);
        Assert.Equal(0.5164, m.BothTeamsScore(), 4);
        Assert.Equal(0.2640, m.Total().Over(3.5m), 4);
        Assert.True(draw > PoissonMath.Outcome(1.6, 1.0).Draw);
    }

    [Fact]
    public void ScoreMatrix_DixonColes_KeepsTotalGoalsMean()
    {
        // Düzeltme takım marjinallerini değiştirmez; toplam gol ortalaması λ_ev + λ_dep olarak kalır.
        Assert.Equal(2.6, new ScoreMatrix(1.6, 1.0, rho: -0.1).Total().Mean, 3);
    }

    [Fact]
    public void NegativeBinomial_KeepsMean_AndWidensTails()
    {
        var nb = CountDistribution.NegativeBinomial(4.6, 20);

        Assert.Equal(4.6, nb.Mean, 3);
        // Python: P(X > 3) = 0.64738, P(X > 5) = 0.32036 (Poisson: 0.67430, 0.31420)
        Assert.Equal(0.6474, nb.Over(3.5m), 4);
        Assert.Equal(0.3204, nb.Over(5.5m), 4);
        Assert.True(nb.Quantile(0.90) >= CountDistribution.Poisson(4.6).Quantile(0.90));
    }

    [Fact]
    public void NegativeBinomial_WithZeroDispersion_IsPoisson()
    {
        Assert.Equal(PoissonMath.Over(10.2, 9.5m), CountDistribution.NegativeBinomial(10.2, 0).Over(9.5m), 6);
    }

    [Fact]
    public void Projection_UsesConfiguredModels()
    {
        var ai = new AiStatsResponse("x", "x", new AiMetric(1.6, "a"), new AiMetric(1.0, "b"), new AiMetric(1.1, "c"),
            new AiMetric(10.2, "d"), new AiMetric(4.6, "e"), new AiMetric(0.2, "f"));

        var plain = ProjectionBuilder.Build(ai, new ForecastOptions { DixonColesRho = 0, CornersDispersion = 0, YellowCardsDispersion = 0 }, NullLogger.Instance);
        var tuned = ProjectionBuilder.Build(ai, new ForecastOptions(), NullLogger.Instance);

        Assert.True(tuned.Result.Draw > plain.Result.Draw);
        // Sarı kart 3.5 Üst: Poisson %67, negatif binom %65 (uçlarda daha temkinli)
        Assert.Equal(67, plain.Stats.Single(s => s.Metric == StatMetric.YellowCards).Lines.Single(l => l.Line == 3.5m).Over);
        Assert.Equal(65, tuned.Stats.Single(s => s.Metric == StatMetric.YellowCards).Lines.Single(l => l.Line == 3.5m).Over);
        // Korner aralığı genişler: 6-14 → 6-15
        Assert.Equal((6, 15), (tuned.Stats.Single(s => s.Metric == StatMetric.Corners).RangeMin, tuned.Stats.Single(s => s.Metric == StatMetric.Corners).RangeMax));
    }
}