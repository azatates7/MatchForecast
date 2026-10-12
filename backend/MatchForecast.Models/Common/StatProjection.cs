namespace MatchForecast.Models.Common;

/*
 * Bir maç istatistiğinin tahmini.
 * - Expected: Modelin verdiği beklenen değer (λ); toplam gol için ev + deplasman.
 * - RangeMin / RangeMax: Poisson dağılımının ortadaki %80'i (10. ve 90. yüzdelik).
 * - Lines: Alt/Üst çizgilerinin olasılıkları; korner için her zaman boştur.
 */
public sealed record StatProjection(
    StatMetric Metric,
    string Label,
    decimal Expected,
    int RangeMin,
    int RangeMax,
    IReadOnlyList<LineProbability> Lines,
    string Reasoning);