using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Controllers;

/*
 * AI kullanım ve tahmini maliyet raporu (tahmin üretimi ve eksik oyuncu araması; arama maliyeti dahil). Yalnızca ai-usage.view yetkisi (Admin örtük olarak sahip) ile açılır;
 * misafir ve normal kullanıcılar 403 alır.
 * Gruplama bellekte yapılır: CreatedAt İstanbul saatine dönüştürülerek okunur, gün sınırı DB'deki UTC'ye göre kaymaz.
 * Satır sayısı küçüktür (günde birkaç yüz çağrı, en fazla 90 gün).
 */
[ApiController]
[Route("api/ai-usage")]
[RequirePermission(PermissionKeys.AiUsageView)]
public sealed class AiUsageController(AppDbContext db, IOptions<AbsencesOptions> absenceOptions) : ControllerBase
{
    private const int MaxDays = 90;

    /// <summary>Son {days} günün (bugün dahil) AI kullanımı. days 1–90 arası, varsayılan 30.</summary>
    [HttpGet]
    public async Task<ActionResult<AiUsageReportResponse>> Get([FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var to = DateOnly.FromDateTime(DateTime.Now);
        var from = to.AddDays(1 - days);
        var fromTime = from.ToDateTime(TimeOnly.MinValue);

        var rows = await db.AiUsages.AsNoTracking()
            .Where(x => x.CreatedAt >= fromTime)
            .Select(x => new { x.CreatedAt, x.Purpose, x.Provider, x.Model, x.Success, x.InputTokens, x.OutputTokens, x.SearchQueries, x.DurationMs, x.EstimatedCostUsd })
            .ToListAsync(ct);

        var daily = rows
            .GroupBy(x => (Date: DateOnly.FromDateTime(x.CreatedAt), x.Purpose, x.Provider, x.Model))
            .Select(g => new AiUsageDailyResponse(
                g.Key.Date,
                g.Key.Purpose,
                g.Key.Provider,
                g.Key.Model,
                g.Count(),
                g.Count(x => !x.Success),
                g.Sum(x => (long)(x.InputTokens ?? 0)),
                g.Sum(x => (long)(x.OutputTokens ?? 0)),
                g.Sum(x => x.SearchQueries),
                g.Sum(x => x.EstimatedCostUsd)))
            .OrderByDescending(x => x.Date).ThenBy(x => x.Purpose).ThenBy(x => x.Provider).ThenBy(x => x.Model)
            .ToList();

        var totals = new AiUsageTotalsResponse(
            rows.Count,
            rows.Count(x => !x.Success),
            rows.Sum(x => (long)(x.InputTokens ?? 0)),
            rows.Sum(x => (long)(x.OutputTokens ?? 0)),
            rows.Sum(x => x.EstimatedCostUsd),
            rows.Count == 0 ? 0 : (int)rows.Average(x => x.DurationMs),
            rows.Sum(x => x.SearchQueries));

        // Aylık arama sayacı seçilen dönemden bağımsızdır (ör. 7 gün seçiliyken de ayın tamamı).
        var monthStart = new DateTime(to.Year, to.Month, 1);
        var monthSearches = await db.AiUsages.AsNoTracking().Where(x => x.CreatedAt >= monthStart).SumAsync(x => x.SearchQueries, ct);
        var opt = absenceOptions.Value;

        return new AiUsageReportResponse(from, to, totals, daily,
            new AiUsageSearchMonthResponse(monthSearches, Math.Max(0, opt.MonthlySearchLimit), Math.Max(0, opt.FreeSearchesPerMonth)));
    }
}