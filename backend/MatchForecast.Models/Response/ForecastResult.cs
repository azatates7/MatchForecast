using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

/// <param name="MissingPlayers">
/// Tahmin üretilirken prompt'a giren eksik oyuncular (sakat, cezalı, şüpheli). null: veri alınamadı; boş liste: bildirilen eksik yok. Ekranda gösterilir; modelin hangi bilgiyle çalıştığını kullanıcı da görür.
/// </param>
public sealed record ForecastResult(
    MatchSummary Match,
    LocalizedText Summary,
    OutcomeProbabilities Outcomes,
    IReadOnlyList<StatProjection> Stats,
    IReadOnlyList<Prediction> Predictions,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<MissingPlayer>? MissingPlayers = null);