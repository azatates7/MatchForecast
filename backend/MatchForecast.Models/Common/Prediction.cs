namespace MatchForecast.Models.Common;

public sealed record Prediction(
    int Rank,
    string Label,
    string Market,
    string Selection,
    decimal Odd,
    decimal ImpliedProbability,
    int Confidence,
    string Reasoning);
