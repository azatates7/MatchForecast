using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

/*
 * GET /api/accuracy: seçilen dönemde (başlama saatine göre) tahmin edilen maçların sonuçlarla karşılaştırması.
 * Oranlar yüzde (0-100, 1 ondalık); örnek yoksa null. Tarihler İstanbul saatidir.
 * - Maç sonucu (1X2): en yüksek olasılıklı sonucun tutma oranı ve Brier skoru (0 = kusursuz, 2 = en kötü;
 *   üç sonuca da %33 veren tahminci 0,667 alır, bunun altı tahminin işe yaradığını gösterir).
 * - Seçilen tahminler: "En olası N tahmin" listesindeki her seçenek ayrı sayılır. Kalibrasyon: güven aralığına göre
 *   gerçekleşme oranı; iyi kalibre edilmiş bir modelde "%70" denen seçeneklerin ~%70'i tutar.
 * - Karar verilemeyen seçenekler (sarı kart istatistiği veya ilk yarı skoru olmayan maçlar) sayılmaz (UnsettledPicks).
 */
public sealed record AccuracyReportResponse(
    DateOnly From,
    DateOnly To,
    AccuracyTotalsResponse Totals,
    IReadOnlyList<AccuracyBucketResponse> Calibration,
    IReadOnlyList<AccuracyMarketResponse> Markets,
    IReadOnlyList<AccuracyGroupResponse> ByAbsences,
    IReadOnlyList<AccuracyMatchResponse> Recent);

/// <param name="Forecasts">Dönemde başlamış, tahmini olan maçlar.</param>
/// <param name="Pending">Sonucu henüz alınmamış (devam ediyor veya toplanmadı).</param>
/// <param name="Void">Ertelenen / iptal edilen maçlar.</param>
/// <param name="Excluded">Değerlendirme dışı: tahmin maç başladıktan sonra üretilmiş (yenilenmiş) veya kayıt okunamadı.</param>
public sealed record AccuracyTotalsResponse(
    int Forecasts,
    int Evaluated,
    int Pending,
    int Void,
    int Excluded,
    int ResultHits,
    double? ResultAccuracy,
    double? BrierScore,
    int Picks,
    int PickHits,
    double? PickHitRate,
    double? AverageConfidence,
    int UnsettledPicks);

/// <summary>Güven aralığı [From, To] içindeki seçilen tahminler.</summary>
public sealed record AccuracyBucketResponse(int From, int To, int Picks, int Hits, double? HitRate, double? AverageConfidence);

public sealed record AccuracyMarketResponse(string Market, LocalizedText Label, int Picks, int Hits, double? HitRate, double? AverageConfidence);

/// <param name="Key">"WithAbsences": tahmin eksik oyuncu listesiyle üretildi; "WithoutAbsences": liste yoktu / alınamadı.</param>
public sealed record AccuracyGroupResponse(string Key, int Matches, int ResultHits, double? ResultAccuracy, int Picks, int PickHits, double? PickHitRate);

/// <param name="PredictedResult">"Home" | "Draw" | "Away": en yüksek olasılıklı sonuç.</param>
public sealed record AccuracyMatchResponse(
    int FixtureId,
    DateTime Kickoff,
    string League,
    string HomeTeam,
    string AwayTeam,
    string Score,
    string? HalfTimeScore,
    string PredictedResult,
    int PredictedProbability,
    bool ResultHit,
    int Picks,
    int PickHits,
    bool HadAbsences);