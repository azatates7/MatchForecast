namespace MatchForecast.Api.Models;

public sealed record MatchSummary(
    int Id,
    DateTimeOffset Kickoff,
    string League,
    string Country,
    string HomeTeam,
    string AwayTeam,
    string Status);

public sealed record OddOption(string Value, decimal Odd);

public sealed record Market(int Id, string Name, IReadOnlyList<OddOption> Options);

public sealed record MatchOdds(int FixtureId, string Bookmaker, IReadOnlyList<Market> Markets);

public sealed record Prediction(
    int Rank,
    string Label,
    string Market,
    string Selection,
    decimal Odd,
    decimal ImpliedProbability,
    int Confidence,
    string Reasoning);

public sealed record ForecastResult(
    MatchSummary Match,
    string Bookmaker,
    string Summary,
    IReadOnlyList<Prediction> Predictions,
    DateTimeOffset GeneratedAt);

/// <summary>Beklenen durumlar (maç yok, oran yok, dış servis hatası) için kullanıcıya gösterilebilir hata.</summary>
public sealed class ForecastException(string message, int statusCode = StatusCodes.Status502BadGateway)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
