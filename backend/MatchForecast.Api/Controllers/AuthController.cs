using MatchForecast.Api.Services;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using MatchForecast.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MatchForecast.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous] // Fallback policy tüm endpoint'leri korur; token almak için bu controller açık kalmalı.
public sealed class AuthController(JwtTokenService tokenService) : ControllerBase
{
    /// <summary>JWT access token üretir. Diğer tüm endpoint'ler bu token'ı "Bearer" olarak ister.</summary>
    [HttpGet("token")]
    [EnableRateLimiting(RateLimitOptions.TokenPolicy)] // IP başına TokenWindowSeconds içinde en fazla TokenPermitLimit istek (varsayılan dakikada 10); aşılırsa 429.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // GET yanıtı tarayıcı/proxy cache'ine düşmesin.
    public ActionResult<TokenResponse> GetToken() => Ok(tokenService.CreateToken());
}