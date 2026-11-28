using MatchForecast.Models.Common;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.Security;
using MatchForecast.Api.Services.Security.Guest;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MatchForecast.Api.Controllers;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(IOddsProvider odds, ForecastService forecastService, IRedisCacheService cache) : ControllerBase
{
    // Toplu uçlar en fazla bu kadar maç işler: popüler tahminlerde her maç ayrı bir AI çağrısı olabilir.
    private const int MaxPopularCount = 10;

    /// <summary>Verilen tarihteki maçları döner. Tarih yoksa veya geçersizse bugün kullanılır. Misafir: yalnızca bugünün Süper Lig maçları; matches.any-date yetkisi olmayanlar: bugün ve yarın.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetMatches([FromQuery] string? date, CancellationToken ct)
    {
        if (GuestAccess.IsGuest(User))
            return Ok(GuestAccess.Filter(await odds.GetMatchesAsync(GuestAccess.Today, ct)));

        var day = ParseDate(date);
        if (!MatchDateAccess.IsAllowed(User, day))
            return DateNotAllowed();

        var result = await odds.GetMatchesAsync(day, ct);
        return Ok(result);
    }

    /*
     * Tahmini önbellekte hazır olan (anında açılan) maçların ID'leri; liste bunları işaretler. Maç listesi önbellekten okunur, her maç için yalnızca Redis'te anahtar varlığına bakılır: AI veya ek API-Football isteği yapılmaz.
     */
    [HttpGet("analyzed")]
    public async Task<ActionResult<IReadOnlyList<int>>> GetAnalyzed([FromQuery] string? date, CancellationToken ct)
    {
        IReadOnlyList<MatchSummary> matches;
        if (GuestAccess.IsGuest(User))
            matches = GuestAccess.Filter(await odds.GetMatchesAsync(GuestAccess.Today, ct));
        else
        {
            var day = ParseDate(date);
            if (!MatchDateAccess.IsAllowed(User, day))
                return DateNotAllowed();
            matches = await odds.GetMatchesAsync(day, ct);
        }

        var checks = await Task.WhenAll(matches.Select(async m => (m.Id, Exists: await cache.ExistsAsync(ForecastService.CacheKey(m.Id)))));
        return Ok(checks.Where(x => x.Exists).Select(x => x.Id).ToList());
    }

    /// <summary>Günün popüler maçlarını döner (varsayılan 10).</summary>
    [HttpGet("popular")]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetPopular([FromQuery] string? date, [FromQuery] int? count, CancellationToken ct)
    {
        if (GuestAccess.IsGuest(User))
            return Ok(GuestAccess.Filter(await odds.GetPopularMatchesAsync(GuestAccess.Today, ClampCount(count), ct)));

        var day = ParseDate(date);
        if (!MatchDateAccess.IsAllowed(User, day))
            return DateNotAllowed();

        var result = await odds.GetPopularMatchesAsync(day, ClampCount(count), ct);
        return Ok(result);
    }

    /// <summary>Günün popüler maçları için toplu tahmin üretir.</summary>
    [HttpGet("popular/forecasts")]
    [EnableRateLimiting(RateLimitOptions.AiPolicy)]
    [Authorize(Policy = GuestAccess.NotGuestPolicy)] // Toplu AI çağrısı; misafire kapalı.
    public async Task<ActionResult<IReadOnlyList<ForecastResult>>> GetPopularForecasts(
        [FromQuery] string? date, [FromQuery] int? count, [FromQuery] bool? refresh, CancellationToken ct)
    {
        var day = ParseDate(date);
        if (!MatchDateAccess.IsAllowed(User, day))
            return DateNotAllowed();

        var result = await forecastService.GetPopularForecastsAsync(day, ClampCount(count), CanRefresh(refresh), ct);
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
                return Problem(statusCode: StatusCodes.Status404NotFound, detail: Lang.T("Maç bulunamadı.", "Match not found."));
        }

        var result = await forecastService.GetForecastAsync(id, CanRefresh(refresh), ct);
        return Ok(result);
    }

    private ObjectResult DateNotAllowed() => Problem(statusCode: StatusCodes.Status400BadRequest,
        detail: Lang.T("Yalnızca bugünün ve yarının maçları görüntülenebilir.", "Only today's and tomorrow's matches can be viewed."));

    private static int ClampCount(int? count) => Math.Clamp(count ?? MaxPopularCount, 1, MaxPopularCount);

    /*
     * Önbelleği atlayıp AI'ı yeniden çağırmak forecast.refresh yetkisi (Admin dahil) ister; diğerleri için refresh yok sayılır.
     * Frontend'in "tekrar dene" butonu (refresh=true) yalnızca hata sonrası görünür; o durumda cache'te kayıt olmadığından
     * normal kullanıcı için de AI yeniden çağrılır, yani davranış bozulmaz.
     */
    private bool CanRefresh(bool? refresh) => refresh == true && PermissionPolicy.HasPermission(User, PermissionKeys.ForecastRefresh);

    private static DateOnly ParseDate(string? date) =>
        DateOnly.TryParse(date, out var d) ? d : GuestAccess.Today; // Tarih yoksa İstanbul'a göre bugün.
}