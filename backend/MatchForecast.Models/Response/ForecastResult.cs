using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

/// <param name="Absences">
/// Tahmin üretilirken prompt'a giren eksik oyuncu raporu (maç günü resmi açıklamalardan). Ekranda da gösterilir;
/// modelin hangi bilgiyle çalıştığını kullanıcı da görür. null: eski kayıt (rapor yok).
/// </param>
public sealed record ForecastResult(
    MatchSummary Match,
    LocalizedText Summary,
    OutcomeProbabilities Outcomes,
    IReadOnlyList<StatProjection> Stats,
    IReadOnlyList<Prediction> Predictions,
    DateTimeOffset GeneratedAt,
    AbsenceReport? Absences = null);