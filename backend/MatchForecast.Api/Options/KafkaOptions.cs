namespace MatchForecast.Api.Options;

// Kafka ayarları. Enabled=false (varsayılan) iken Kafka hiç kullanılmaz: tahminler eskisi gibi doğrudan veritabanına yazılır, producer/consumer kaydedilmez. Böylece testler ve Kafka'sız yerel geliştirme değişmeden çalışır.
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    public bool Enabled { get; set; }

    // Docker ağı içinde "kafka:9092", yerelde "localhost:9092".
    public string BootstrapServers { get; set; } = "localhost:9092";

    // Üretilen her tahmin bu topic'e yazılır; key = fixtureId (aynı maçın olayları aynı partition'a düşer, sırası korunur).
    public string ForecastGeneratedTopic { get; set; } = "matchforecast.forecast.generated";

    // Kalıcı olarak işlenemeyen mesajlar (bozuk JSON, tekrar denemelere rağmen DB hatası) buraya taşınır.
    public string ForecastGeneratedDlqTopic { get; set; } = "matchforecast.forecast.generated.dlq";

    // DB'ye yazan consumer'ın grubu. Birden fazla API instance'ı aynı grupta olur; her mesajı yalnızca biri işler.
    public string PersistenceGroupId { get; set; } = "matchforecast-forecast-persistence";

    // Consumer açılışta topic'leri bu partition sayısıyla oluşturur (varsa dokunmaz). Tek broker'da replikasyon 1'dir.
    public int TopicPartitions { get; set; } = 3;
    public short TopicReplicationFactor { get; set; } = 1;

    /*
     * Producer bu süre içinde broker'dan onay alamazsa vazgeçer ve tahmin doğrudan DB'ye yazılır (fallback).
     * Kafka kapalıyken kullanıcı isteği en fazla bu kadar uzar; kısa tutulur.
     */
    public int MessageTimeoutMs { get; set; } = 5000;

    // Consumer'da DB yazımı bu kadar denenir (denemeler arası 2 sn, 4 sn, ...); sonra mesaj DLQ'ya gider.
    public int MaxPersistAttempts { get; set; } = 3;
}