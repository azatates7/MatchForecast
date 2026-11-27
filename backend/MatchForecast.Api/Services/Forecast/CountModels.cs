namespace MatchForecast.Api.Services.Forecast;

/*
 * Sayı (gol, korner, kart) dağılımının tablosu: Pmf[k] = P(X = k). Toplamı 1'e normalize edilir.
 * Üst / alt ve aralık hesapları dağılımın türünden bağımsız olarak bu tablo üzerinden yapılır; böylece Poisson, negatif binom ve Dixon-Coles skor tablosundan türetilen toplam gol aynı şekilde kullanılır.
 */
public sealed class CountDistribution
{
    private readonly double[] _pmf;

    public CountDistribution(double[] pmf)
    {
        var sum = pmf.Sum();
        _pmf = sum > 0 ? pmf.Select(p => p / sum).ToArray() : [1.0];
    }

    public double Mean => _pmf.Select((p, k) => p * k).Sum();

    // "n.5 Üst" = en az n+1 = P(X > floor(line)).
    public double Over(decimal line)
    {
        var k = (int)Math.Floor(line);
        double sum = 0;
        for (var i = Math.Max(0, k + 1); i < _pmf.Length; i++)
            sum += _pmf[i];
        return Math.Clamp(sum, 0, 1);
    }

    // P(X <= k) >= probability olan en küçük k; aralık için 0.10 ve 0.90 kullanılır.
    public int Quantile(double probability)
    {
        double sum = 0;
        for (var k = 0; k < _pmf.Length; k++)
        {
            sum += _pmf[k];
            if (sum >= probability - 1e-12) return k;
        }
        return _pmf.Length - 1;
    }

    public static CountDistribution Poisson(double lambda) => new(Table(lambda, k => PoissonMath.Pmf(lambda, k)));

    /*
     * Negatif binom (ortalama λ, dağılım parametresi r): Var = λ + λ²/r. r büyüdükçe Poisson'a yaklaşır.
     * Korner ve kartlar maçtan maça Poisson'un varsaydığından fazla değişir (ör. tempolu maçlarda kart yağar);
     * NB bu fazla değişkenliği hesaba katar, üst/alt yüzdeleri uçlarda daha temkinli olur ve aralık genişler.
     * r <= 0 verilirse Poisson kullanılır.
     */
    public static CountDistribution NegativeBinomial(double mean, double r)
    {
        if (r <= 0 || mean <= 0)
            return Poisson(mean);

        var q = r / (r + mean); // "başarı" olasılığı
        return new(Table(mean, k => NbPmf(k, r, q), extraSpread: Math.Sqrt(mean + mean * mean / r)));
    }

    // P(X = k) = Γ(k + r) / (k! Γ(r)) · q^r · (1 − q)^k ; taşmasın diye tekrarlamalı çarpımla.
    private static double NbPmf(int k, double r, double q)
    {
        var p = Math.Pow(q, r);
        for (var i = 1; i <= k; i++)
            p *= (i - 1 + r) / i * (1 - q);
        return p;
    }

    // Tablo, olasılık kütlesinin pratikte tamamını kapsayacak uzunlukta (ortalama + 12 standart sapma + 10).
    private static double[] Table(double mean, Func<int, double> pmf, double? extraSpread = null)
    {
        var sd = extraSpread ?? Math.Sqrt(Math.Max(mean, 0));
        var max = (int)Math.Ceiling(Math.Max(mean, 0) + 12 * sd + 10);
        return Enumerable.Range(0, max + 1).Select(pmf).ToArray();
    }
}

/*
 * Ev ve deplasman gollerinin birlikte dağılımı (skor tablosu, 0-10 gol).
 * Dixon-Coles düzeltmesi: bağımsız Poisson düşük skorlu sonuçları (0-0, 1-1) eksik, 1-0 ve 0-1'i fazla hesaplar.
 * rho < 0 iken 0-0 ve 1-1 artar, 1-0 ve 0-1 azalır; beraberlik ve alt seçenekleri gerçekçi düzeye gelir.
 * Takımların kendi gol dağılımı (marjinaller) değişmez, yalnızca birlikte dağılım düzeltilir. rho = 0 → bağımsız Poisson.
 */
public sealed class ScoreMatrix
{
    private readonly double[,] _p;

    public ScoreMatrix(double homeLambda, double awayLambda, double rho = 0)
    {
        var n = PoissonMath.MaxGoals;
        _p = new double[n + 1, n + 1];
        double total = 0;
        for (var h = 0; h <= n; h++)
        for (var a = 0; a <= n; a++)
        {
            var p = PoissonMath.Pmf(homeLambda, h) * PoissonMath.Pmf(awayLambda, a) * Tau(h, a, homeLambda, awayLambda, rho);
            _p[h, a] = Math.Max(p, 0);
            total += _p[h, a];
        }
        for (var h = 0; h <= n; h++)
        for (var a = 0; a <= n; a++)
            _p[h, a] /= total;
    }

    private static double Tau(int h, int a, double l, double m, double rho) => (h, a) switch
    {
        (0, 0) => 1 - l * m * rho,
        (0, 1) => 1 + l * rho,
        (1, 0) => 1 + m * rho,
        (1, 1) => 1 - rho,
        _ => 1
    };

    public (double Home, double Draw, double Away) Outcome()
    {
        double home = 0, draw = 0, away = 0;
        Each((h, a, p) =>
        {
            if (h > a) home += p;
            else if (h == a) draw += p;
            else away += p;
        });
        return (home, draw, away);
    }

    public double BothTeamsScore()
    {
        double sum = 0;
        Each((h, a, p) => { if (h > 0 && a > 0) sum += p; });
        return sum;
    }

    // Toplam gol dağılımı (ev + deplasman).
    public CountDistribution Total()
    {
        var n = PoissonMath.MaxGoals;
        var pmf = new double[2 * n + 1];
        Each((h, a, p) => pmf[h + a] += p);
        return new CountDistribution(pmf);
    }

    private void Each(Action<int, int, double> f)
    {
        var n = PoissonMath.MaxGoals;
        for (var h = 0; h <= n; h++)
        for (var a = 0; a <= n; a++)
            f(h, a, _p[h, a]);
    }
}