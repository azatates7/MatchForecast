namespace MatchForecast.Models.Response;

public sealed record MatchSummary(
    int Id,
    DateTimeOffset Kickoff,
    string League,
    string Country,
    string HomeTeam,
    string AwayTeam,
    string Status);
