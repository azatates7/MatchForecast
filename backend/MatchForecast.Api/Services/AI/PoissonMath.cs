namespace MatchForecast.Api.Services;

/*
 * Poisson dağılımı hesapları. Her metrik (gol, korner, kart), beklenen değeri λ olan bir Poisson dağılımı kabul edilir.
 * Bilinen zayıflıklar: bağımsız Poisson beraberliği biraz düşük, korner aralığını biraz dar hesaplar. İleride Dixon-Coles
 * düzeltmesi veya negatif binom dağılımı eklenirse yalnızca bu sınıf değişir.
 */
public static class PoissonMath
{
    // Skor tablosunda takım başına üst sınır; takım λ'sı en fazla 5 iken 10'dan fazla gol olasılığı ihmal edilebilir, kalan kütle normalize edilir.
    public const int MaxGoals = 10;

    // P(X = k), faktöriyel taşmasın diye çarpımla hesaplanır.
    public static double Pmf(double lambda, int k)
    {
        if (k < 0) return 0;
        var p = Math.Exp(-lambda);
        for (var i = 1; i <= k; i++)
            p *= lambda / i;
        return p;
    }

    // P(X <= k)
    public static double Cdf(double lambda, int k)
    {
        if (k < 0) return 0;
        double sum = 0, p = Math.Exp(-lambda);
        for (var i = 0; i <= k; i++)
        {
            if (i > 0) p *= lambda / i;
            sum += p;
        }
        return Math.Min(sum, 1);
    }

    // "n.5 Üst" = en az n+1 = 1 - P(X <= n). Örn. λ = 2.5 için 2.5 Üst ≈ 0.456.
    public static double Over(double lambda, decimal line) => 1 - Cdf(lambda, (int)Math.Floor(line));

    // P(X <= k) >= probability olan en küçük k; aralık için 0.10 ve 0.90 kullanılır.
    public static int Quantile(double lambda, double probability)
    {
        double sum = 0, p = Math.Exp(-lambda);
        for (var k = 0; k < 200; k++)
        {
            if (k > 0) p *= lambda / k;
            sum += p;
            if (sum >= probability) return k;
        }
        return 200;
    }

    // Ev ve deplasman golleri bağımsız kabul edilir; 0-10 gol skor tablosu üzerinden 1/X/2 olasılıkları.
    public static (double Home, double Draw, double Away) Outcome(double homeLambda, double awayLambda)
    {
        double home = 0, draw = 0, away = 0;
        for (var h = 0; h <= MaxGoals; h++)
        {
            var ph = Pmf(homeLambda, h);
            for (var a = 0; a <= MaxGoals; a++)
            {
                var p = ph * Pmf(awayLambda, a);
                if (h > a) home += p;
                else if (h == a) draw += p;
                else away += p;
            }
        }

        var total = home + draw + away;
        return (home / total, draw / total, away / total);
    }

    // İki takımın da en az 1 gol atma olasılığı.
    public static double BothTeamsScore(double homeLambda, double awayLambda) =>
        (1 - Math.Exp(-homeLambda)) * (1 - Math.Exp(-awayLambda));

    public static int ToPercent(double probability) =>
        (int)Math.Clamp(Math.Round(probability * 100, MidpointRounding.AwayFromZero), 0, 100);

    // Üç olasılığı toplamı tam 100 olan yüzdelere çevirir; yuvarlama farkı en büyük değere eklenir.
    public static (int A, int B, int C) ToPercents((double A, double B, double C) values)
    {
        int[] r = [ToPercent(values.A), ToPercent(values.B), ToPercent(values.C)];
        var largest = Array.IndexOf(r, r.Max());
        r[largest] += 100 - r.Sum();
        return (r[0], r[1], r[2]);
    }
}