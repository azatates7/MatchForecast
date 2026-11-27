namespace MatchForecast.Data.Entity;

/*
 * Tahmini yapılmış bir maçın kesin sonucu (isabet takibi için). ResultSettlementWorker, başlama saati geçmiş ve sonucu
 * henüz olmayan tahminler için API-Football'dan doldurur. Maç başına tek satır.
 * - Status: API-Football fixture.status.short. "FT" | "AET" | "PEN" ise skorlar doludur; ertelenen/iptal edilen
 *   ("PST", "CANC", "ABD", "AWD", "WO") maçlarda skor null'dır ve değerlendirmeye girmez.
 * - Skorlar normal süre (90 dk) skorudur (score.fulltime); uzatma ve penaltılar marketleri etkilemez.
 */
public sealed class MatchResult
{
    public int Id { get; set; }
    public int FixtureId { get; set; }
    public string Status { get; set; } = string.Empty;

    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    public int? HalfTimeHomeGoals { get; set; }
    public int? HalfTimeAwayGoals { get; set; }

    public DateTime CreatedAt { get; set; }
}