namespace MatchForecast.Models.Response;

/// <summary>
/// Kafka DLQ (kalıcı olarak işlenemeyen tahmin mesajları) raporu. Enabled=false: Kafka kapalı, liste boş.
/// Total: DLQ'da saklanan mesaj sayısı (panelden silinenler hariç); Pending: henüz yeniden işlenmemiş olanlar. Messages en yeni önce, en fazla
/// MaxMessages kadar (Truncated=true ise daha eski kayıtlar listede yok).
/// </summary>
public sealed record KafkaDlqReportResponse(
    bool Enabled,
    string Topic,
    int Total,
    int Pending,
    bool Truncated,
    IReadOnlyList<KafkaDlqMessageResponse> Messages);

/// <summary>
/// DLQ'daki tek mesaj. Match/Kickoff mesaj gövdesinden okunur (gövde bozuksa null; Preview ilk karakterleri gösterir).
/// Error: consumer'ın eklediği hata nedeni. ReplayedAt: panelden yeniden işlendiği an (işlenmediyse null).
/// </summary>
public sealed record KafkaDlqMessageResponse(
    int Partition,
    long Offset,
    DateTimeOffset Timestamp,
    int FixtureId,
    string? Match,
    DateTimeOffset? Kickoff,
    string? Error,
    string? Preview,
    DateTimeOffset? ReplayedAt);

/// <summary>"Tümünü sil" sonucu: Kafka'dan kalıcı olarak silinen kayıt sayısı.</summary>
public sealed record KafkaDlqPurgeResponse(int Deleted);

/// <summary>Yeniden işleme sonucu: ana topic'e geri gönderilen, zaten işlenmiş olduğu için atlanan ve gönderilemeyen mesaj sayıları.</summary>
public sealed record KafkaDlqReplayResponse(int Replayed, int Skipped, int Failed);