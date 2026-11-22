using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Kafka;

/*
 * "forecast.generated" topic'ini okuyup tahminleri veritabanına (forecasts tablosu) yazar.
 *
 * Teslim garantisi: at-least-once. Offset yalnızca kayıt başarılı olunca commit edilir; uygulama kayıttan sonra,
 * commit'ten önce kapanırsa mesaj tekrar gelir. Bu güvenlidir çünkü ForecastStore.SaveAsync idempotent'tir
 * (fixture_id başına tek satır; varsa günceller).
 *
 * Hata yönetimi:
 * - Bozuk mesaj (JSON okunamıyor): tekrar denemek işe yaramaz, doğrudan DLQ'ya.
 * - DB hatası: MaxPersistAttempts kez artan beklemeyle denenir, olmazsa DLQ'ya. DLQ'ya da yazılamazsa (Kafka gitti)
 *   offset geri sarılır (Seek) ve mesaj sonra yeniden işlenir; hiçbir tahmin sessizce kaybolmaz.
 *
 * Consume() bloklayan bir çağrıdır; döngü ayrı bir thread'de çalışır ki host'un açılışı beklemesin.
 */
public sealed class ForecastPersistenceConsumer(
    IServiceScopeFactory scopeFactory,
    IProducer<int, string> producer,
    IOptions<KafkaOptions> options,
    ILogger<ForecastPersistenceConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private readonly KafkaOptions _opt = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
    {
        try
        {
            await RunAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hata dışarı kaçarsa BackgroundService host'u durdurur. Kalıcı kayıt kritik değil (publisher DB fallback'i var), yalnızca loglanır.
            logger.LogError(ex, "Kafka forecast consumer stopped unexpectedly.");
        }
    }, stoppingToken);

    private async Task RunAsync(CancellationToken ct)
    {
        await EnsureTopicsAsync(ct);

        using var consumer = KafkaClientFactory.CreateConsumer(_opt, logger);
        consumer.Subscribe(_opt.ForecastGeneratedTopic);
        logger.LogInformation("Kafka consumer started. Topic: {Topic}, Group: {Group}", _opt.ForecastGeneratedTopic, _opt.PersistenceGroupId);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<int, string>? result = null;
                try
                {
                    result = consumer.Consume(ct);
                    if (result?.Message is null)
                        continue;

                    await HandleAsync(result, ct);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    // Broker geçici olarak yok, topic henüz yok vb. librdkafka kendisi yeniden bağlanır.
                    logger.LogWarning(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
                    await DelayAsync(RetryDelay, ct);
                }
                catch (Exception ex)
                {
                    // Mesaj işlenemedi ve DLQ'ya da taşınamadı: commit edilmez, offset geri sarılır, biraz sonra tekrar denenir.
                    logger.LogError(ex, "Kafka message could not be processed; it will be retried. Offset: {Offset}", result?.TopicPartitionOffset);
                    TrySeek(consumer, result);
                    await DelayAsync(RetryDelay, ct);
                }
            }
        }
        finally
        {
            // Grup üyeliğinden düzgün çıkar; partition'lar beklemeden diğer instance'lara dağıtılır.
            consumer.Close();
        }
    }

    private async Task HandleAsync(ConsumeResult<int, string> result, CancellationToken ct)
    {
        ForecastResult forecast;
        try
        {
            forecast = Deserialize(result.Message.Value);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Invalid forecast message at {Offset}; moving to DLQ.", result.TopicPartitionOffset);
            await SendToDlqAsync(result, ex, ct);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await PersistAsync(forecast, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= _opt.MaxPersistAttempts)
                {
                    logger.LogError(ex, "Forecast for fixture {FixtureId} could not be saved after {Attempts} attempts; moving to DLQ.", forecast.Match.Id, attempt);
                    await SendToDlqAsync(result, ex, ct);
                    return;
                }

                logger.LogWarning(ex, "Saving forecast for fixture {FixtureId} failed (attempt {Attempt}/{Max}).", forecast.Match.Id, attempt, _opt.MaxPersistAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }

    /*
     * JSON geçerli olsa da maç bilgisi yoksa (ör. "{}") mesaj bozuk sayılır ve JsonException ile DLQ'ya gider.
     * Bu kontrol olmazsa kayıt sırasında NullReferenceException oluşur; catch bloğundaki log da Match.Id'ye eriştiği için ikinci hata yeniden deneme/DLQ akışını atlar ve aynı mesaj sonsuza kadar tekrar işlenip partition'ı kilitlerdi.
     */
    public static ForecastResult Deserialize(string json)
    {
        var forecast = JsonSerializer.Deserialize<ForecastResult>(json, KafkaClientFactory.JsonOptions)
                       ?? throw new JsonException("Empty forecast message.");

        return forecast.Match is null
            ? throw new JsonException("Forecast message has no match.")
            : forecast;
    }

    // ForecastStore ve AppDbContext scoped; singleton consumer her mesaj için kendi scope'unu açar.
    public async Task PersistAsync(ForecastResult forecast, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ForecastStore>();
        await store.SaveAsync(forecast, ct);
    }

    private async Task SendToDlqAsync(ConsumeResult<int, string> result, Exception error, CancellationToken ct)
    {
        // Orijinal başlıklar korunur, hata nedeni eklenir; DLQ'daki mesaj sorun giderildikten sonra elle yeniden işlenebilir.
        var headers = result.Message.Headers ?? new Headers();
        headers.Add(KafkaClientFactory.ErrorHeader, Encoding.UTF8.GetBytes($"{error.GetType().Name}: {error.Message}"));

        await producer.ProduceAsync(_opt.ForecastGeneratedDlqTopic, new Message<int, string>
        {
            Key = result.Message.Key,
            Value = result.Message.Value,
            Headers = headers
        }, ct);
    }

    /*
     * Topic'leri yoksa oluşturur (varsa dokunmaz). Kafka henüz ayağa kalkmadıysa (docker compose'da API önce açılabilir)
     * erişilene kadar bekler; bu sırada API çalışmaya devam eder, publisher da DB fallback'ine düşer.
     */
    private async Task EnsureTopicsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var admin = KafkaClientFactory.CreateAdminClient(_opt);
                await admin.CreateTopicsAsync(
                    [
                        new TopicSpecification { Name = _opt.ForecastGeneratedTopic, NumPartitions = _opt.TopicPartitions, ReplicationFactor = _opt.TopicReplicationFactor },
                        new TopicSpecification { Name = _opt.ForecastGeneratedDlqTopic, NumPartitions = 1, ReplicationFactor = _opt.TopicReplicationFactor }
                    ],
                    new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                return;
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
            {
                return; // Topic'ler zaten var.
            }
            catch (Exception ex) when (ex is KafkaException)
            {
                logger.LogWarning(ex, "Kafka is not reachable at {Servers}; retrying topic setup in {Delay}s.", _opt.BootstrapServers, RetryDelay.TotalSeconds);
                await DelayAsync(RetryDelay, ct);
            }
        }
    }

    // Rebalance sırasında partition bu instance'tan alınmışsa Seek hata verir; o durumda mesajı yeni sahibi zaten işler.
    private void TrySeek(IConsumer<int, string> consumer, ConsumeResult<int, string>? result)
    {
        if (result is null)
            return;
        try
        {
            consumer.Seek(result.TopicPartitionOffset);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not rewind to offset {Offset}.", result.TopicPartitionOffset);
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}