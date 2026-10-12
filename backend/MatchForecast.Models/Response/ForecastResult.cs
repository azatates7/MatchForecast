using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

public sealed record ForecastResult(
    MatchSummary Match,
    string Summary,
    OutcomeProbabilities Outcomes,
    IReadOnlyList<StatProjection> Stats,
    IReadOnlyList<Prediction> Predictions,
    DateTimeOffset GeneratedAt);