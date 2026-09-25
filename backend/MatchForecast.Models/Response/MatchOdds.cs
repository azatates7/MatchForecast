using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

public sealed record MatchOdds(int FixtureId, string Bookmaker, IReadOnlyList<Market> Markets);
