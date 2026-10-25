using MatchForecast.Api.Options;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Security.Guest;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MatchForecast.Api.Controllers;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(IOddsProvider odds, ForecastService forecastService) : ControllerBase
{
    // Toplu uçlar en fazla bu kadar maç işler: popüler tahminlerde her maç ayrı bir AI çağrısı olabilir.
    private const int MaxPopularCount = 10;

    /// <summary>Verilen tarihteki maçları döner. Tarih yoksa veya geçersizse bugün kullanılır. Misafir: yalnızca bugünün Süper Lig maçları.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetMatches([FromQuery] string? date, CancellationToken ct)
    {
        if (GuestAccess.IsGuest(User))
            return Ok(GuestAccess.Filter(await odds.GetMatchesAsync(GuestAccess.Today, ct)));

        var result = await odds.GetMatchesAsync(ParseDate(date), ct);
        return Ok(result);
    }

    /// <summary>Günün popüler maçlarını döner (varsayılan 10).</summary>
    [HttpGet("popular")]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetPopular([FromQuery] string? date, [FromQuery] int? count, CancellationToken ct)
    {
        if (GuestAccess.IsGuest(User))
            return Ok(GuestAccess.Filter(await odds.GetPopularMatchesAsync(GuestAccess.Today, ClampCount(count), ct)));

        var result = await odds.GetPopularMatchesAsync(ParseDate(date), ClampCount(count), ct);
        return Ok(result);
    }

    /// <summary>Günün popüler maçları için toplu tahmin üretir.</summary>
    [HttpGet("popular/forecasts")]
    [EnableRateLimiting(RateLimitOptions.AiPolicy)]
    [Authorize(Policy = GuestAccess.NotGuestPolicy)] // Toplu AI çağrısı; misafire kapalı.
    public async Task<ActionResult<IReadOnlyList<ForecastResult>>> GetPopularForecasts(
        [FromQuery] string? date, [FromQuery] int? count, [FromQuery] bool? refresh, CancellationToken ct)
    {
        var result = await forecastService.GetPopularForecastsAsync(ParseDate(date), ClampCount(count), CanRefresh(refresh), ct);
        return Ok(result);
    }

    /// <summary>Maç için AI tahmini üretir (gol/korner/kart beklentileri ve 10 tahmin); refresh=true ise önbelleği atlar.</summary>
    [HttpGet("{id:int}/forecast")]
    [EnableRateLimiting(RateLimitOptions.AiPolicy)]
    public async Task<ActionResult<ForecastResult>> GetForecast(int id, [FromQuery] bool? refresh, CancellationToken ct)
    {
        // Misafir yalnızca bugünün Süper Lig maçlarını analiz edebilir; diğerleri yokmuş gibi 404 döner (AI çağrılmaz).
        if (GuestAccess.IsGuest(User))
        {
            var match = await odds.GetMatchAsync(id, ct);
            if (match is null || !GuestAccess.IsAllowedToday(match))
                return Problem(statusCode: StatusCodes.Status404NotFound, detail: "Maç bulunamadı.");
        }

        var result = await forecastService.GetForecastAsync(id, CanRefresh(refresh), ct);
        return Ok(result);
    }

    private static int ClampCount(int? count) => Math.Clamp(count ?? MaxPopularCount, 1, MaxPopularCount);

    /*
     * Önbelleği atlayıp AI'ı yeniden çağırmak yalnızca Admin'e açık; diğer kullanıcılar için refresh yok sayılır.
     * Frontend'in "tekrar dene" butonu (refresh=true) yalnızca hata sonrası görünür; o durumda cache'te kayıt olmadığından
     * normal kullanıcı için de AI yeniden çağrılır, yani davranış bozulmaz.
     */
    private bool CanRefresh(bool? refresh) => refresh == true && User.IsInRole(RoleNames.Admin);

    private static DateOnly ParseDate(string? date) =>
        DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
}