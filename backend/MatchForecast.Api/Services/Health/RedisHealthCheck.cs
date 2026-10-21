using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MatchForecast.Api.Services.Health;

/*
 * Redis yalnızca cache: kapalıyken API kaynaktan cevap vermeye devam eder (fail-open). Bu yüzden Redis'e ulaşılamaması
 * Unhealthy değil Degraded sayılır; Degraded HTTP 200 döner ve uygulamayı trafikten çıkarmaz.
 */
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Ping {latency.TotalMilliseconds:0} ms.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Redis'e ulaşılamıyor; cache devre dışı.", ex);
        }
    }
}