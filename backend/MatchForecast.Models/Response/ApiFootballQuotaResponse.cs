namespace MatchForecast.Models.Response;

/// <summary>
/// API-Football günlük istek kotası: son API-Football cevabının başlıklarından (x-ratelimit-requests-limit / x-ratelimit-requests-remaining) okunur. UpdatedAt: bu bilginin alındığı an; o andan sonraki istekler sayılmamıştır.
/// </summary>
public sealed record ApiFootballQuotaResponse(int Limit, int Remaining, DateTimeOffset UpdatedAt);