namespace MatchForecast.Models.Common;

// Bir Alt/Üst çizgisinin olasılıkları (yüzde); Over + Under her zaman 100'dür.
public sealed record LineProbability(decimal Line, int Over, int Under);