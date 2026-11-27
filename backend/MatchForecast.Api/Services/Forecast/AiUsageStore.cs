using MatchForecast.Api.Options;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Forecast;

// AI çağrılarının kaydı (ai_usages). ForecastService her tahmin çağrısından, GeminiAbsenceResearcher her aramadan sonra bir kez çağırır.
public interface IAiUsageStore
{
    Task RecordAsync(int fixtureId, AiCompletion? completion, int? failedStatusCode, TimeSpan duration, CancellationToken ct);

    /// <summary>Eksik oyuncu araması (Gemini + Google araması). completion null ise çağrı başarısızdır.</summary>
    Task RecordSearchAsync(int fixtureId, string model, AiCompletion? completion, int searchQueries, int? failedStatusCode,
        TimeSpan duration, CancellationToken ct);

    /// <summary>Bu ay (İstanbul takvimi) yapılan Google araması sorgusu sayısı (Absences:MonthlySearchLimit için).</summary>
    Task<int> SearchQueriesThisMonthAsync(CancellationToken ct);
}

public sealed class AiUsageStore(AppDbContext db, IOptions<AiOptions> aiOptions, IOptions<AbsencesOptions> absenceOptions) : IAiUsageStore
{
    public async Task RecordAsync(int fixtureId, AiCompletion? completion, int? failedStatusCode, TimeSpan duration, CancellationToken ct)
    {
        var ai = aiOptions.Value;
        var (provider, model) = ai.CurrentModel();
        var (inputPrice, outputPrice) = ai.CurrentPrices();

        await SaveAsync(new AiUsage
        {
            FixtureId = fixtureId,
            Purpose = AiUsagePurpose.Forecast,
            Provider = provider,
            Model = model,
            Success = completion is not null,
            StatusCode = failedStatusCode,
            InputTokens = completion?.InputTokens,
            OutputTokens = completion?.OutputTokens,
            DurationMs = Milliseconds(duration),
            EstimatedCostUsd = Cost(completion?.InputTokens, inputPrice) + Cost(completion?.OutputTokens, outputPrice),
            CreatedAt = DateTime.Now
        }, ct);
    }

    /*
     * Arama her zaman Gemini ile yapılır (tahmin sağlayıcısı başka olsa da): token fiyatı Ai:Gemini'den alınır.
     * Arama maliyeti: ayın ilk Absences:FreeSearchesPerMonth sorgusu ücretsiz, sonrası Absences:SearchPricePer1K / 1000.
     * Ay, kayıtların saat dilimine (İstanbul) göre hesaplanır; Google'ın sayaç sınırıyla birkaç saat kayabilir.
     */
    public async Task RecordSearchAsync(int fixtureId, string model, AiCompletion? completion, int searchQueries, int? failedStatusCode,
        TimeSpan duration, CancellationToken ct)
    {
        var gemini = aiOptions.Value.Gemini;
        var now = DateTime.Now;
        searchQueries = Math.Max(0, searchQueries);

        await SaveAsync(new AiUsage
        {
            FixtureId = fixtureId,
            Purpose = AiUsagePurpose.Absences,
            Provider = "Gemini",
            Model = model,
            Success = completion is not null,
            StatusCode = failedStatusCode,
            InputTokens = completion?.InputTokens,
            OutputTokens = completion?.OutputTokens,
            SearchQueries = searchQueries,
            DurationMs = Milliseconds(duration),
            EstimatedCostUsd = Cost(completion?.InputTokens, gemini.InputPricePer1M)
                               + Cost(completion?.OutputTokens, gemini.OutputPricePer1M)
                               + await SearchCostAsync(searchQueries, now, ct),
            CreatedAt = now
        }, ct);
    }

    // Bu ay daha önce yapılan sorgular ücretsiz kotadan düşülür; yalnızca kotayı aşan kısım ücretlenir.
    private async Task<decimal> SearchCostAsync(int queries, DateTime now, CancellationToken ct)
    {
        var opt = absenceOptions.Value;
        if (queries == 0 || opt.SearchPricePer1K <= 0)
            return 0m;

        var usedThisMonth = await SearchQueriesSinceAsync(new DateTime(now.Year, now.Month, 1), ct);

        var free = Math.Max(0, opt.FreeSearchesPerMonth);
        var billable = Math.Max(0, usedThisMonth + queries - free) - Math.Max(0, usedThisMonth - free);
        return Math.Round(billable * opt.SearchPricePer1K / 1000m, 6);
    }

    public Task<int> SearchQueriesThisMonthAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        return SearchQueriesSinceAsync(new DateTime(now.Year, now.Month, 1), ct);
    }

    private Task<int> SearchQueriesSinceAsync(DateTime from, CancellationToken ct) =>
        db.AiUsages.AsNoTracking().Where(x => x.CreatedAt >= from).SumAsync(x => x.SearchQueries, ct);

    private async Task SaveAsync(AiUsage usage, CancellationToken ct)
    {
        db.AiUsages.Add(usage);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Aynı scope'taki sonraki kayıt (ör. ForecastStore) bu başarısız satırı tekrar yazmaya çalışmasın.
            db.Entry(usage).State = EntityState.Detached;
            throw;
        }
    }

    private static int Milliseconds(TimeSpan duration) => (int)Math.Min(duration.TotalMilliseconds, int.MaxValue);

    // Fiyatlar 1M token başına USD'dir (Ai:{Provider}:InputPricePer1M / OutputPricePer1M).
    private static decimal Cost(int? tokens, decimal pricePer1M) =>
        tokens is null or <= 0 || pricePer1M <= 0 ? 0m : Math.Round(tokens.Value * pricePer1M / 1_000_000m, 6);
}