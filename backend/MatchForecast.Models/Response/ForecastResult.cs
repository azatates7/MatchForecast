using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

public sealed record ForecastResult(
    MatchSummary Match,
    string Bookmaker,
    string Summary,
    IReadOnlyList<Prediction> Predictions,
    DateTimeOffset GeneratedAt);
