using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.ApiFootball;

public interface IOddsProvider
{
    Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<MatchSummary>> GetPopularMatchesAsync(DateOnly date, int count, CancellationToken ct);
    Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct);

    /// <summary>Maça çıkamayacak (sakat, cezalı vb.) veya şüpheli oyuncular. Bu veriyi sağlamayan kaynaklar boş liste döner.</summary>
    Task<IReadOnlyList<MissingPlayer>> GetMissingPlayersAsync(int fixtureId, CancellationToken ct);
}