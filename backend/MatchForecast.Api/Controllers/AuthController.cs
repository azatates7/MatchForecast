using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Security;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Controllers;

/*
 * Kimlik bilgileri query string yerine body'de taşınır (POST): URL'ler sunucu/proxy erişim loglarına ve tarayıcı geçmişine
 * düşer, body ise LoggingMiddleware'de "password"/"refreshToken" alanları maskelenerek loglanır.
 */
[ApiController]
[Route("api/auth")]
[AllowAnonymous] // Fallback policy tüm endpoint'leri korur; token almak için bu controller açık kalmalı.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // Token yanıtları tarayıcı/proxy cache'ine düşmesin.
public sealed class AuthController(AuthService auth, GoogleIdTokenValidator google, IOptions<AuthOptions> authOptions) : ControllerBase
{
    /// <summary>Frontend'in giriş ekranı için ayarlar (ör. Google Client ID; boşsa Google butonu gösterilmez).</summary>
    [HttpGet("config")]
    public ActionResult<AuthConfigResponse> GetConfig()
    {
        var clientId = authOptions.Value.GoogleClientId;
        return new AuthConfigResponse(string.IsNullOrWhiteSpace(clientId) ? null : clientId);
    }

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

    /// <summary>Yeni hesap açar ("User" rolüyle) ve doğrudan giriş yapar.</summary>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitOptions.TokenPolicy)]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var (token, error) = await auth.RegisterAsync(request, ct);
        if (token is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: error);

        return token;
    }

    /// <summary>Google ile giriş: Google Identity Services butonunun verdiği ID token doğrulanır, bizim token çiftimiz döner.</summary>
    [HttpPost("google")]
    [EnableRateLimiting(RateLimitOptions.TokenPolicy)]
    public async Task<ActionResult<TokenResponse>> Google(GoogleLoginRequest request, CancellationToken ct)
    {
        var googleUser = await google.ValidateAsync(request.IdToken, ct);
        var token = googleUser is null ? null : await auth.GoogleLoginAsync(googleUser, ct);
        if (token is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "Google ile giriş yapılamadı.");

        return token;
    }

    /// <summary>Şifre sıfırlama bağlantısı gönderir. E-posta kayıtlı olsun olmasın 204 döner.</summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitOptions.TokenPolicy)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.RequestPasswordResetAsync(request.Email, ct);
        return NoContent();
    }

    /// <summary>E-postadaki bağlantının token'ı ile yeni şifre belirler; tüm açık oturumlar kapanır.</summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitOptions.TokenPolicy)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        if (!await auth.ResetPasswordAsync(request.Token, request.Password, ct))
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Bağlantı geçersiz veya süresi dolmuş. Yeni bir sıfırlama bağlantısı isteyin.");

        return NoContent();
    }

    /// <summary>Çıkış: refresh token'ı iptal eder. Access token süresi dolana kadar geçerli kalır (kısa ömürlüdür).</summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshTokenRequest request, CancellationToken ct)
    {
        await auth.RevokeAsync(request.RefreshToken, ct);
        return NoContent();
    }
}