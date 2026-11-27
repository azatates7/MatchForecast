namespace MatchForecast.Models.Response;

// GET /api/ai-usage: seçilen dönemin toplamları ve gün/amaç/sağlayıcı/model kırılımı (en yeni gün önce). Tarihler İstanbul saatidir.
// Purpose: "Forecast" (tahmin üretimi) | "Absences" (eksik oyuncu araması). SearchQueries: Google araması sorgu sayısı.
public sealed record AiUsageReportResponse(
    DateOnly From,
    DateOnly To,
    AiUsageTotalsResponse Totals,
    IReadOnlyList<AiUsageDailyResponse> Daily);

public sealed record AiUsageTotalsResponse(
    int Calls,
    int Failures,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd,
    int AverageDurationMs,
    int SearchQueries);

public sealed record AiUsageDailyResponse(
    DateOnly Date,
    string Purpose,
    string Provider,
    string Model,
    int Calls,
    int Failures,
    long InputTokens,
    long OutputTokens,
    int SearchQueries,
    decimal EstimatedCostUsd);