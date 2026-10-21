using MatchForecast.Api.Services.Security;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatchForecast.Api.Controllers;

// Kayıt ekranında (henüz token yokken) kullanıldığı için anonim. Hiçbir veriye erişmez, hiçbir şey saklamaz;
// yalnızca CPU açısından ucuz bir rastgele değer üretir.
[ApiController]
[Route("api/password")]
[AllowAnonymous]
public sealed class PasswordController : ControllerBase
{
    /// <summary>Rastgele, güvenli bir şifre üretir (büyük/küçük harf, rakam ve sembol içerir). Hiçbir yerde saklanmaz.</summary>
    /// <param name="length">Şifre uzunluğu (8–128, varsayılan 16).</param>
    /// <returns>Şifre ve uzunluğu.</returns>
    /// <remarks>
    /// Örnek: `GET /api/password?length=20`<br/>
    /// </remarks>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // Şifre tarayıcı/proxy cache'ine düşmesin.
    public ActionResult<PasswordResponse> Generate([FromQuery] int length = PasswordGenerator.DefaultLength)
    {
        if (length is < PasswordGenerator.MinLength or > PasswordGenerator.MaxLength)
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"Şifre uzunluğu {PasswordGenerator.MinLength} ile {PasswordGenerator.MaxLength} arasında olmalı.");

        return Ok(new PasswordResponse(PasswordGenerator.Generate(length), length));
    }
}