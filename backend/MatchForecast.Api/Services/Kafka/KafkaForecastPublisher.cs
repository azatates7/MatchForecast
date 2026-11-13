using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Kafka;

/*
 * Kafka açıkken IForecastStore bu sınıftır. ForecastService değişmez: hâlâ store.SaveAsync çağırır, ama tahmin artık
 * doğrudan veritabanına değil "forecast.generated" topic'ine yazılır. DB'ye yazımı ForecastPersistenceConsumer yapar.
 *
 * - Key = fixtureId: aynı maçın olayları aynı partition'a düşer ve sırayla işlenir (refresh ile üretilen yeni tahmin,
 *   eskisinin üzerine yazılır, araya girmez).
 * - ProduceAsync broker onayı (acks=all) gelene kadar bekler; dönüş = mesaj Kafka'da kalıcı.
 * - Fallback: Kafka'ya ulaşılamazsa (MessageTimeoutMs içinde onay yok) tahmin eskisi gibi doğrudan DB'ye yazılır.
 *   O da başarısız olursa exception ForecastService'e çıkar; orada zaten loglanıp yutuluyor (fail-open).
 */
public sealed class KafkaForecastPublisher(
    IProducer<int, string> producer,
    ForecastStore databaseStore,
    IOptions<KafkaOptions> options,
    ILogger<KafkaForecastPublisher> logger) : IForecastStore
{
    private readonly KafkaOptions _opt = options.Value;

    public async Task SaveAsync(ForecastResult result, CancellationToken ct)
    {
        var message = new Message<int, string>
        {
            Key = result.Match.Id,
            Value = JsonSerializer.Serialize(result, KafkaClientFactory.JsonOptions),
            Headers = new Headers
            {
                { KafkaClientFactory.EventTypeHeader, Encoding.UTF8.GetBytes(KafkaClientFactory.ForecastGeneratedEventType) },
                { KafkaClientFactory.SchemaVersionHeader, Encoding.UTF8.GetBytes(KafkaClientFactory.ForecastGeneratedSchemaVersion) }
            }
        };

        try
        {
            var delivery = await producer.ProduceAsync(_opt.ForecastGeneratedTopic, message, ct);
            logger.LogDebug("Forecast for fixture {FixtureId} published to {TopicPartitionOffset}.", result.Match.Id, delivery.TopicPartitionOffset);
        }
        catch (Exception ex) when (ex is ProduceException<int, string> or KafkaException)
        {
            logger.LogWarning(ex, "Forecast for fixture {FixtureId} could not be published to Kafka; writing directly to the database.", result.Match.Id);
            await databaseStore.SaveAsync(result, ct);
        }
    }
}