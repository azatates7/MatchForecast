using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Kafka;

/*
 * Yönetim paneli için DLQ okuma ve yeniden işleme (replay).
 *
 * Okuma: DLQ topic'i, uygulamanın consumer grubundan bağımsız, commit yapmayan geçici bir consumer'la partition'lara doğrudan atanarak (Assign) okunur. Hiçbir offset değişmez; okuma DLQ'daki mesajları silmez veya tüketmez.
 *
 * Yeniden işleme: mesaj olduğu gibi (key, gövde, event-type / schema-version başlıkları) ana topic'e geri yazılır;
 * ForecastPersistenceConsumer onu normal akışta tekrar kaydeder. Eski "error" başlığı atılır, "replayed-from" eklenir.
 * Kayıt idempotent olduğu için (fixture başına tek satır) aynı mesajın iki kez işlenmesi zarar vermez; yine de panelde çift tıklamayı önlemek için yeniden işlenen mesaj Redis'te işaretlenir (DLQ saklama süresinden biraz uzun).
 * Redis önbelleği temizlenirse bu işaretler de silinir; mesajlar tekrar "bekliyor" görünür, işlenmeleri yine güvenlidir.
 * Mesaj tekrar başarısız olursa consumer onu yeni bir DLQ kaydı olarak ekler (hata nedeniyle birlikte).
 *
 * Silme: Kafka tek bir mesajı silmeye izin vermez (bir partition'da yalnızca belirli bir offset'ten öncekilerin hepsi silinebilir). Bu yüzden iki ayrı işlem vardır:
 * - Tek kayıt "sil": kayıt Redis'te gizlendi olarak işaretlenir, listeden ve sayılardan çıkar; Kafka'dan saklama süresi dolunca kalıcı olarak silinir. Önbellek temizlenirse gizlenen kayıtlar listede yeniden görünür.
 * - "Tümünü sil": DeleteRecords ile DLQ'daki bütün kayıtlar (her partition'da son offset'e kadar) Kafka'dan kalıcı silinir.
 */
public sealed class KafkaDlqService(
    IServiceProvider services,
    IRedisCacheService cache,
    IOptions<KafkaOptions> options,
    ILogger<KafkaDlqService> logger)
{
    public const int MaxMessages = 200;
    public const string ReplayedFromHeader = "replayed-from";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReplayMarkTtl = TimeSpan.FromDays(8);
    private const int PreviewLength = 300;

    private KafkaOptions Opt => options.Value;

    public async Task<KafkaDlqReportResponse> GetReportAsync(CancellationToken ct)
    {
        if (!Opt.Enabled)
            return new KafkaDlqReportResponse(false, Opt.ForecastGeneratedDlqTopic, 0, 0, false, []);

        var (records, total) = await Task.Run(() => ReadLatest(MaxMessages, ct), ct);
        var messages = new List<KafkaDlqMessageResponse>(records.Count);
        var dismissed = 0;
        foreach (var r in records.OrderByDescending(r => r.Message.Timestamp.UnixTimestampMs))
        {
            if (await cache.ExistsAsync(DismissKey(r.Partition.Value, r.Offset.Value)))
            {
                dismissed++;
                continue;
            }
            messages.Add(ToResponse(r, await cache.GetAsync<DateTimeOffset?>(ReplayKey(r.Partition.Value, r.Offset.Value))));
        }

        // Gizlenen (silinen) kayıtlar sayılmaz. Liste kesildiyse eski kayıtların durumu bilinmez; bekliyor sayılır.
        var visibleTotal = total - dismissed;
        var unlisted = Math.Max(0, total - records.Count);
        var pending = messages.Count(m => m.ReplayedAt is null) + unlisted;
        return new KafkaDlqReportResponse(true, Opt.ForecastGeneratedDlqTopic, visibleTotal, pending, unlisted > 0, messages);
    }

    // Tek kaydı listeden kaldırır (Kafka tek mesaj silmeye izin vermez; saklama süresi dolunca kalıcı silinir).
    public async Task DismissAsync(int partition, long offset)
    {
        EnsureEnabled();
        await cache.SetAsync(DismissKey(partition, offset), DateTimeOffset.Now, ReplayMarkTtl);
        logger.LogInformation("DLQ message {Partition}/{Offset} dismissed from the admin panel.", partition, offset);
    }

    // DLQ'daki bütün kayıtları Kafka'dan kalıcı olarak siler (her partition'da son offset'e kadar). Dönüş: silinen kayıt sayısı.
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        EnsureEnabled();
        var targets = await Task.Run(() =>
        {
            using var consumer = CreateReader();
            return Partitions()
                .Select(p => (Tp: p, W: consumer.QueryWatermarkOffsets(p, TimeSpan.FromSeconds(5))))
                .Where(x => x.W.High.Value > x.W.Low.Value)
                .ToList();
        }, ct);
        if (targets.Count == 0)
            return 0;

        using var admin = KafkaClientFactory.CreateAdminClient(Opt);
        await admin.DeleteRecordsAsync(targets.Select(x => new TopicPartitionOffset(x.Tp, x.W.High)));
        var deleted = (int)targets.Sum(x => x.W.High.Value - x.W.Low.Value);
        logger.LogWarning("DLQ purged from the admin panel: {Count} messages deleted.", deleted);
        return deleted;
    }

    public async Task<KafkaDlqReplayResponse> ReplayAsync(int partition, long offset, CancellationToken ct)
    {
        EnsureEnabled();
        if (await cache.ExistsAsync(ReplayKey(partition, offset)))
            return new KafkaDlqReplayResponse(0, 1, 0);

        var record = await Task.Run(() => ReadOne(partition, offset, ct), ct)
                     ?? throw new ForecastException(Lang.T(
                         "Kayıt bulunamadı; saklama süresi dolmuş olabilir.", "Message not found; its retention period may have expired."),
                         StatusCodes.Status404NotFound);

        return await ProduceAsync(record, ct)
            ? new KafkaDlqReplayResponse(1, 0, 0)
            : new KafkaDlqReplayResponse(0, 0, 1);
    }

    // Listede görünen (en yeni MaxMessages) ve henüz yeniden işlenmemiş tüm kayıtlar.
    public async Task<KafkaDlqReplayResponse> ReplayAllAsync(CancellationToken ct)
    {
        EnsureEnabled();
        var (records, _) = await Task.Run(() => ReadLatest(MaxMessages, ct), ct);
        int replayed = 0, skipped = 0, failed = 0;
        foreach (var r in records.OrderBy(r => r.Message.Timestamp.UnixTimestampMs))
        {
            // Silinen (gizlenen) kayıtlar yeniden işlenmez ve sayılmaz.
            if (await cache.ExistsAsync(DismissKey(r.Partition.Value, r.Offset.Value)))
                continue;
            if (await cache.ExistsAsync(ReplayKey(r.Partition.Value, r.Offset.Value)))
                skipped++;
            else if (await ProduceAsync(r, ct))
                replayed++;
            else
                failed++;
        }
        return new KafkaDlqReplayResponse(replayed, skipped, failed);
    }

    private async Task<bool> ProduceAsync(ConsumeResult<int, string> record, CancellationToken ct)
    {
        var producer = services.GetRequiredService<IProducer<int, string>>();
        var headers = new Headers();
        if (record.Message.Headers is { } original)
            foreach (var h in original)
                if (h.Key != KafkaClientFactory.ErrorHeader)
                    headers.Add(h.Key, h.GetValueBytes());
        headers.Add(ReplayedFromHeader, Encoding.UTF8.GetBytes(record.TopicPartitionOffset.ToString()));

        try
        {
            await producer.ProduceAsync(Opt.ForecastGeneratedTopic, new Message<int, string>
            {
                Key = record.Message.Key,
                Value = record.Message.Value,
                Headers = headers
            }, ct);
            await cache.SetAsync(ReplayKey(record.Partition.Value, record.Offset.Value), (DateTimeOffset?)DateTimeOffset.Now, ReplayMarkTtl);
            logger.LogInformation("DLQ message {Offset} replayed to {Topic}.", record.TopicPartitionOffset, Opt.ForecastGeneratedTopic);
            return true;
        }
        catch (Exception ex) when (ex is ProduceException<int, string> or KafkaException)
        {
            logger.LogError(ex, "DLQ message {Offset} could not be replayed.", record.TopicPartitionOffset);
            return false;
        }
    }

    private void EnsureEnabled()
    {
        if (!Opt.Enabled)
            throw new ForecastException(Lang.T("Kafka kapalı.", "Kafka is disabled."), StatusCodes.Status409Conflict);
    }

    private static string ReplayKey(int partition, long offset) => $"kafka:dlq:replayed:{partition}:{offset}";
    private static string DismissKey(int partition, long offset) => $"kafka:dlq:dismissed:{partition}:{offset}";

    // Her partition'ın son kayıtlarını okur (toplamda en yeni `max` kadar). Dönüş: kayıtlar ve DLQ'daki toplam mesaj sayısı.
    private (List<ConsumeResult<int, string>> Records, int Total) ReadLatest(int max, CancellationToken ct)
    {
        using var consumer = CreateReader();
        var partitions = Partitions();
        var ranges = partitions
            .Select(p => (Tp: p, W: consumer.QueryWatermarkOffsets(p, TimeSpan.FromSeconds(5))))
            .Where(x => x.W.High.Value > x.W.Low.Value)
            .ToList();
        var total = (int)ranges.Sum(x => x.W.High.Value - x.W.Low.Value);
        if (ranges.Count == 0)
            return ([], 0);

        // Her partition'dan en fazla `max` son kayıt okunur; zaman sırasına göre en yeni `max` tanesi tutulur.
        consumer.Assign(ranges.Select(x => new TopicPartitionOffset(x.Tp, Math.Max(x.W.Low.Value, x.W.High.Value - max))));
        var ends = ranges.ToDictionary(x => x.Tp.Partition.Value, x => x.W.High.Value);
        var records = new List<ConsumeResult<int, string>>();
        var deadline = DateTime.UtcNow + ReadTimeout;
        while (ends.Count > 0 && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var r = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (r is null)
                continue;
                
            if (!r.IsPartitionEOF && r.Message is not null)
                records.Add(r);
            if (r.IsPartitionEOF || r.Offset.Value + 1 >= ends.GetValueOrDefault(r.Partition.Value))
                ends.Remove(r.Partition.Value);
        }

        var latest = records.OrderByDescending(r => r.Message.Timestamp.UnixTimestampMs).Take(max).ToList();
        return (latest, total);
    }

    private ConsumeResult<int, string>? ReadOne(int partition, long offset, CancellationToken ct)
    {
        using var consumer = CreateReader();
        consumer.Assign(new TopicPartitionOffset(Opt.ForecastGeneratedDlqTopic, partition, offset));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var r = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (r is null)
                continue;
            // İstenen offset silinmişse (saklama süresi) consumer daha ileriden başlar: kayıt yok sayılır.
            return r.IsPartitionEOF || r.Offset.Value != offset ? null : r;
        }
        return null;
    }

    private List<TopicPartition> Partitions()
    {
        using var admin = KafkaClientFactory.CreateAdminClient(Opt);
        var topic = admin.GetMetadata(Opt.ForecastGeneratedDlqTopic, TimeSpan.FromSeconds(5)).Topics.FirstOrDefault();
        if (topic is null || topic.Error.IsError)
            return [];
        return topic.Partitions.Select(p => new TopicPartition(Opt.ForecastGeneratedDlqTopic, p.PartitionId)).ToList();
    }

    // Commit yapmayan, gruba katılmayan okuyucu: Assign ile kullanılır, uygulamanın consumer'ını etkilemez.
    private IConsumer<int, string> CreateReader() =>
        new ConsumerBuilder<int, string>(new ConsumerConfig
        {
            BootstrapServers = Opt.BootstrapServers,
            GroupId = "matchforecast-dlq-admin",
            ClientId = "matchforecast-api-dlq-admin",
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            EnablePartitionEof = true,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();

    // Gövdeden maç bilgisi okunur; okunamazsa (bozuk mesaj) gövdenin başı gösterilir.
    public static KafkaDlqMessageResponse ToResponse(ConsumeResult<int, string> r, DateTimeOffset? replayedAt)
    {
        var headers = r.Message.Headers;
        var error = headers is not null && headers.TryGetLastBytes(KafkaClientFactory.ErrorHeader, out var e) ? Encoding.UTF8.GetString(e) : null;
        var (match, kickoff, preview) = Describe(r.Message.Value);
        return new KafkaDlqMessageResponse(
            r.Partition.Value,
            r.Offset.Value,
            r.Message.Timestamp.UtcDateTime == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(r.Message.Timestamp.UtcDateTime).ToLocalTime(),
            r.Message.Key,
            match,
            kickoff,
            error,
            preview,
            replayedAt);
    }

    public static (string? Match, DateTimeOffset? Kickoff, string? Preview) Describe(string? value)
    {
        try
        {
            var forecast = ForecastPersistenceConsumer.Deserialize(value ?? "");
            return ($"{forecast.Match.HomeTeam} – {forecast.Match.AwayTeam}", forecast.Match.Kickoff, null);
        }
        catch (JsonException)
        {
            var text = value ?? "";
            return (null, null, text.Length > PreviewLength ? text[..PreviewLength] + "…" : text);
        }
    }
}