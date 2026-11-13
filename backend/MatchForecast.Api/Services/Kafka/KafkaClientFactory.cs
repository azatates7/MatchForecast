using System.Text.Json;
using Confluent.Kafka;
using MatchForecast.Api.Options;

namespace MatchForecast.Api.Services.Kafka;

// Producer / consumer / admin client ayarları tek yerde; publisher, consumer ve health check aynı ayarları kullanır.
public static class KafkaClientFactory
{
    // Mesaj gövdesi API yanıtı ve forecasts.result_json ile aynı biçimdedir (camelCase).
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Mesaj başlıkları: ileride gövde biçimi değişirse consumer eski/yeni mesajları ayırt edebilsin.
    public const string EventTypeHeader = "event-type";
    public const string SchemaVersionHeader = "schema-version";
    public const string ErrorHeader = "error";
    public const string ForecastGeneratedEventType = "ForecastGenerated";
    public const string ForecastGeneratedSchemaVersion = "1";

    public static IProducer<int, string> CreateProducer(KafkaOptions o, ILogger logger) =>
        new ProducerBuilder<int, string>(new ProducerConfig
        {
            BootstrapServers = o.BootstrapServers,
            ClientId = "matchforecast-api",
            // Mesaj tüm in-sync replikalara yazılmadan "gönderildi" sayılmaz; idempotence yeniden denemede çift kaydı önler.
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = o.MessageTimeoutMs
        })
        // Broker'a ulaşılamaması gibi hatalar burada da raporlanır; ProduceAsync ayrıca exception fırlatır.
        .SetErrorHandler((_, e) => logger.LogWarning("Kafka producer error: {Code} {Reason}", e.Code, e.Reason))
        .Build();

    public static IConsumer<int, string> CreateConsumer(KafkaOptions o, ILogger logger) =>
        new ConsumerBuilder<int, string>(new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = o.PersistenceGroupId,
            ClientId = "matchforecast-api",
            // Grup ilk kez açıldığında topic'in başından okur; açılış öncesi yazılan tahminler kaçmaz.
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Offset yalnızca DB'ye yazım (veya DLQ'ya taşıma) başarılı olunca elle commit edilir: at-least-once.
            EnableAutoCommit = false
        })
        .SetErrorHandler((_, e) => logger.LogWarning("Kafka consumer error: {Code} {Reason}", e.Code, e.Reason))
        .Build();

    public static IAdminClient CreateAdminClient(KafkaOptions o) =>
        new AdminClientBuilder(new AdminClientConfig { BootstrapServers = o.BootstrapServers }).Build();
}