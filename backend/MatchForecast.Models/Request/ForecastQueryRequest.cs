namespace MatchForecast.Models.Request;

public sealed record ForecastQueryRequest(int FixtureId, bool Refresh = false);
