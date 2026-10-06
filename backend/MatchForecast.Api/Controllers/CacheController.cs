using MatchForecast.Api.Services;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc;

namespace MatchForecast.Api.Controllers;

[ApiController]
[Route("api/cache")]
public sealed class CacheController(IRedisCacheService cache) : ControllerBase
{
    // Uygulamanın Redis'teki tüm kayıtlarını (maç listesi, oranlar, AI tahminleri) siler.
    [HttpDelete]
    public async Task<ActionResult<CacheClearResponse>> Clear()
    {
        var deleted = await cache.ClearAsync();
        return Ok(new CacheClearResponse(deleted));
    }
}