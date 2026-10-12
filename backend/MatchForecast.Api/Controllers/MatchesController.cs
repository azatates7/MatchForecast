using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc;

namespace MatchForecast.Api.Controllers;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(IOddsProvider odds, ForecastService forecastService) : ControllerBase
{
    /// <summary>Verilen tarihteki maçları döner. Tarih yoksa veya geçersizse bugün kullanılır.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetMatches([FromQuery] string? date, CancellationToken ct)
    {
        var result = await odds.GetMatchesAsync(ParseDate(date), ct);
        return Ok(result);
    }

    /// <summary>Günün popüler maçlarını döner (varsayılan 10).</summary>
    [HttpGet("popular")]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetPopular([FromQuery] string? date, [FromQuery] int? count, CancellationToken ct)
    {
        var result = await odds.GetPopularMatchesAsync(ParseDate(date), count ?? 10, ct);
        return Ok(result);
    }

    /// <summary>Günün popüler maçları için toplu tahmin üretir.</summary>
    [HttpGet("popular/forecasts")]
    public async Task<ActionResult<IReadOnlyList<ForecastResult>>> GetPopularForecasts(
        [FromQuery] string? date, [FromQuery] int? count, [FromQuery] bool? refresh, CancellationToken ct)
    {
        var result = await forecastService.GetPopularForecastsAsync(ParseDate(date), count ?? 10, refresh ?? false, ct);
        return Ok(result);
    }

    /// <summary>Maç için AI tahmini üretir (gol/korner/kart beklentileri ve 10 tahmin); refresh=true ise önbelleği atlar.</summary>
    [HttpGet("{id:int}/forecast")]
    public async Task<ActionResult<ForecastResult>> GetForecast(int id, [FromQuery] bool? refresh, CancellationToken ct)
    {
        var result = await forecastService.GetForecastAsync(id, refresh ?? false, ct);
        return Ok(result);
    }

    private static DateOnly ParseDate(string? date) =>
        DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
}