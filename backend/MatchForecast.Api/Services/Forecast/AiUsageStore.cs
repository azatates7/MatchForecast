using MatchForecast.Api.Options;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Forecast;

// AI çağrılarının kaydı (ai_usages). ForecastService her çağrıdan sonra bir kez çağırır.
public interface IAiUsageStore
{
    Task RecordAsync(int fixtureId, AiCompletion? completion, int? failedStatusCode, TimeSpan duration, CancellationToken ct);
}

public sealed class AiUsageStore(AppDbContext db, IOptions<AiOptions> aiOptions) : IAiUsageStore
{
    public async Task RecordAsync(int fixtureId, AiCompletion? completion, int? failedStatusCode, TimeSpan duration, CancellationToken ct)
    {
        var ai = aiOptions.Value;
        var (provider, model) = ai.CurrentModel();
        var (inputPrice, outputPrice) = ai.CurrentPrices();

        var usage = new AiUsage
        {
            FixtureId = fixtureId,
            Provider = provider,
            Model = model,
            Success = completion is not null,
            StatusCode = failedStatusCode,
            InputTokens = completion?.InputTokens,
            OutputTokens = completion?.OutputTokens,
            DurationMs = (int)Math.Min(duration.TotalMilliseconds, int.MaxValue),
            EstimatedCostUsd = Cost(completion?.InputTokens, inputPrice) + Cost(completion?.OutputTokens, outputPrice),
            CreatedAt = DateTime.Now
        };

        db.AiUsages.Add(usage);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Aynı scope'taki sonraki kayıt (ör. ForecastStore) bu başarısız satırı tekrar yazmaya çalışmasın.
            db.Entry(usage).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            throw;
        }
    }

    // Fiyatlar 1M token başına USD'dir (Ai:{Provider}:InputPricePer1M / OutputPricePer1M).
    private static decimal Cost(int? tokens, decimal pricePer1M) =>
        tokens is null or <= 0 || pricePer1M <= 0 ? 0m : Math.Round(tokens.Value * pricePer1M / 1_000_000m, 6);
}