using MatchForecast.Api.Models;

namespace MatchForecast.Api.Services;

public interface IOddsProvider
{
    Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct);
    Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct);
    Task<MatchOdds?> GetOddsAsync(int fixtureId, CancellationToken ct);
}
