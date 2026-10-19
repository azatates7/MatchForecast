using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Security;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MatchForecast.Api.Controllers;

/*
 * Kimlik bilgileri query string yerine body'de taşınır (POST): URL'ler sunucu/proxy erişim loglarına ve tarayıcı geçmişine
 * düşer, body ise LoggingMiddleware'de "password"/"refreshToken" alanları maskelenerek loglanır.
 */
[ApiController]
[Route("api/auth")]
[AllowAnonymous] // Fallback policy tüm endpoint'leri korur; token almak için bu controller açık kalmalı.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // Token yanıtları tarayıcı/proxy cache'ine düşmesin.
public sealed class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>Kullanıcı adı ve şifre ile access token + refresh token üretir.</summary>
    [HttpPost("token")]
    [EnableRateLimiting(RateLimitOptions.TokenPolicy)] // IP başına dakikada en fazla TokenPermitLimit deneme; şifre denemeye karşı da koruma.
    public async Task<ActionResult<TokenResponse>> GetToken(GetTokenRequest request, CancellationToken ct)
    {
        var token = await auth.LoginAsync(request.Username, request.Password, ct);
        if (token is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "Kullanıcı adı veya şifre hatalı.");

        return token;
    }

    /// <summary>Refresh token ile yeni access token + yeni refresh token alır. Gönderilen refresh token geçersiz olur.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitOptions.TokenPolicy)]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        var token = await auth.RefreshAsync(request.RefreshToken, ct);
        if (token is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "Oturumun süresi doldu, tekrar giriş yapın.");

        return token;
    }

    /// <summary>Çıkış: refresh token'ı iptal eder. Access token süresi dolana kadar geçerli kalır (kısa ömürlüdür).</summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshTokenRequest request, CancellationToken ct)
    {
        await auth.RevokeAsync(request.RefreshToken, ct);
        return NoContent();
    }
}