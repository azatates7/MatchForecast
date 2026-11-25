namespace MatchForecast.Models.Response;

// GET /api/ai-usage: seçilen dönemin toplamları ve gün/sağlayıcı/model kırılımı (en yeni gün önce). Tarihler İstanbul saatidir.
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
    int AverageDurationMs);

public sealed record AiUsageDailyResponse(
    DateOnly Date,
    string Provider,
    string Model,
    int Calls,
    int Failures,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);