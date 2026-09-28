using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services;

public interface IOddsProvider
{
    Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<MatchSummary>> GetPopularMatchesAsync(DateOnly date, int count, CancellationToken ct);
    Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct);
    Task<MatchOdds?> GetOddsAsync(int fixtureId, CancellationToken ct);
}
