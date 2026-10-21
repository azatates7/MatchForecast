using MatchForecast.Data.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MatchForecast.Api.Services.Health;

// Veritabanına (Supabase) bağlanılabiliyor mu? Supabase ücretsiz projesi duraklatıldığında da burada görünür.
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Bağlantı başarılı.")
                : HealthCheckResult.Unhealthy("Veritabanına bağlanılamıyor.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Veritabanına bağlanılamıyor.", ex);
        }
    }
}