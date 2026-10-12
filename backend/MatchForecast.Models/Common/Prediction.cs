namespace MatchForecast.Models.Common;

// Confidence: hesaplanan gerçekleşme olasılığı (0-100); modelin verdiği bir puan değildir.
public sealed record Prediction(
    int Rank,
    string Label,
    string Market,
    string Selection,
    int Confidence,
    string Reasoning);