using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MatchForecast.Api.Services.Health;

/*
 * Redis gibi Kafka da kritik değil: kapalıyken tahminler DB'ye doğrudan yazılır (publisher fallback). Bu yüzden ulaşılamaması Unhealthy değil Degraded sayılır; fallback sessizce çalışırken sorun /healthz/ready'de görünür olur.
 * Yeni bağlantı açmamak için producer'ın bağlantısını kullanan bağımlı admin client kullanılır.
 */
public sealed class KafkaHealthCheck(IProducer<int, string> producer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var admin = new DependentAdminClientBuilder(producer.Handle).Build();
            // GetMetadata senkron ve bloklayıcı; istek thread'ini tutmasın.
            var metadata = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(2)), cancellationToken);
            return metadata.Brokers.Count > 0
                ? HealthCheckResult.Healthy($"{metadata.Brokers.Count} broker.")
                : HealthCheckResult.Degraded("Kafka broker bulunamadı; tahminler doğrudan veritabanına yazılıyor.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Kafka'ya ulaşılamıyor; tahminler doğrudan veritabanına yazılıyor.", ex);
        }
    }
}