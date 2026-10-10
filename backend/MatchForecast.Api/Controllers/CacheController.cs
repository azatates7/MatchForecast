using MatchForecast.Models.Response;
using MatchForecast.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MatchForecast.Api.Services.Redis;

namespace MatchForecast.Api.Controllers;

[ApiController]
[Route("api/cache")]
public sealed class CacheController(IRedisCacheService cache) : ControllerBase
{
    // Uygulamanın Redis'teki tüm kayıtlarını (maç listesi, oranlar, AI tahminleri) siler.
    [HttpDelete]
    [EnableRateLimiting(RateLimitOptions.CacheClearPolicy)] // Tüm istemciler için ortak: 60 dk içinde en fazla 2 kez; aşılırsa 429.
    public async Task<ActionResult<CacheClearResponse>> Clear()
    {
        var deleted = await cache.ClearAsync();
        return Ok(new CacheClearResponse(deleted));
    }
}