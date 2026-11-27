using MatchForecast.Api.Services.Accuracy;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Controllers;

/*
 * Tahmin isabet raporu: dönemde başlamış maçların tahminleri, toplanan sonuçlarla (match_results) karşılaştırılır.
 * Yalnızca accuracy.view yetkisi (Admin örtük olarak sahip) ile açılır. Hesap bellekte yapılır (AccuracyReportBuilder);
 * satır sayısı küçüktür (günde en fazla birkaç yüz tahmin, en fazla 180 gün).
 */
[ApiController]
[Route("api/accuracy")]
[RequirePermission(PermissionKeys.AccuracyView)]
public sealed class AccuracyController(AppDbContext db) : ControllerBase
{
    private const int MaxDays = 180;

    /// <summary>Son {days} günde (bugün dahil, başlama saatine göre) başlamış maçların isabet raporu. days 1–180, varsayılan 30.</summary>
    [HttpGet]
    public async Task<ActionResult<AccuracyReportResponse>> Get([FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var now = DateTime.Now;
        var to = DateOnly.FromDateTime(now);
        var from = to.AddDays(1 - days);
        var fromTime = from.ToDateTime(TimeOnly.MinValue);

        var forecasts = await db.Forecasts.AsNoTracking()
            .Where(f => f.Kickoff >= fromTime && f.Kickoff <= now)
            .Select(f => new AccuracyForecast(f.FixtureId, f.Kickoff, f.League, f.HomeTeam, f.AwayTeam, f.GeneratedAt, f.ResultJson))
            .ToListAsync(ct);

        var ids = forecasts.Select(f => f.FixtureId).ToList();
        var results = await db.MatchResults.AsNoTracking()
            .Where(r => ids.Contains(r.FixtureId))
            .ToDictionaryAsync(r => r.FixtureId, ct);

        return AccuracyReportBuilder.Build(forecasts, results, from, to);
    }
}