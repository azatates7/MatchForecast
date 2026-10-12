using MatchForecast.Api.Services;

namespace MatchForecast.Tests;

// Beklenen değerler Poisson formülünden bağımsız olarak (Python) hesaplanmıştır.
public class PoissonMathTests
{
    [Fact]
    public void Over_MatchesKnownValue()
    {
        // λ = 2.5 için 2.5 Üst = 1 - P(X <= 2) = 0.45619
        Assert.Equal(0.4562, PoissonMath.Over(2.5, 2.5m), 4);
    }

    [Fact]
    public void Pmf_SumsToOne()
    {
        var sum = Enumerable.Range(0, 40).Sum(k => PoissonMath.Pmf(3.0, k));
        Assert.Equal(1.0, sum, 6);
    }

    [Fact]
    public void Quantile_GivesEightyPercentRange()
    {
        Assert.Equal(1, PoissonMath.Quantile(2.6, 0.10));
        Assert.Equal(5, PoissonMath.Quantile(2.6, 0.90));
        Assert.Equal(6, PoissonMath.Quantile(10.2, 0.10));
        Assert.Equal(14, PoissonMath.Quantile(10.2, 0.90));
    }

    [Fact]
    public void Outcome_SumsToOne_AndIsSymmetricForEqualTeams()
    {
        var (home, draw, away) = PoissonMath.Outcome(1.3, 1.3);
        Assert.Equal(1.0, home + draw + away, 9);
        Assert.Equal(home, away, 9);
    }

    [Fact]
    public void Outcome_MatchesKnownValues()
    {
        var (home, draw, away) = PoissonMath.Outcome(1.6, 1.0);
        Assert.Equal(0.5134, home, 4);
        Assert.Equal(0.2500, draw, 4);
        Assert.Equal(0.2366, away, 4);
    }

    [Fact]
    public void BothTeamsScore_MatchesFormula()
    {
        // (1 - e^-1)^2
        Assert.Equal(0.3996, PoissonMath.BothTeamsScore(1.0, 1.0), 4);
    }

    [Fact]
    public void ToPercents_AlwaysSumsToHundred()
    {
        var (a, b, c) = PoissonMath.ToPercents((0.333, 0.333, 0.334));
        Assert.Equal(100, a + b + c);
    }

    [Fact]
    public void ZeroLambda_IsHandled()
    {
        Assert.Equal(0, PoissonMath.Over(0, 0.5m), 9);
        Assert.Equal(0, PoissonMath.Quantile(0, 0.90));
    }
}