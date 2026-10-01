namespace MatchForecast.Models.Response;

/// <param name="Elapsed">Devam eden maçta oynanan dakika (API-Football fixture.status.elapsed); başlamamış maçta null.</param>
/// <param name="ElapsedExtra">Uzatma dakikası (fixture.status.extra), ör. 45+2 için 2; yoksa null.</param>
public sealed record MatchSummary(
    int Id,
    DateTimeOffset Kickoff,
    string League,
    string Country,
    string HomeTeam,
    string AwayTeam,
    string Status,
    int? Elapsed = null,
    int? ElapsedExtra = null);