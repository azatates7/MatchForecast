using MatchForecast.Api.Services;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatchForecast.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous] // Fallback policy tüm endpoint'leri korur; token almak için bu controller açık kalmalı.
public sealed class AuthController(JwtTokenService tokenService) : ControllerBase
{
    /// <summary>Kullanıcı adı/şifre ile JWT access token üretir. Diğer tüm endpoint'ler bu token'ı "Bearer" olarak ister.</summary>
    [HttpPost("token")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> GetToken([FromBody] GetTokenRequest request)
    {
        if (!tokenService.ValidateCredentials(request.Username, request.Password))
        {
            // Frontend (api.ts) ProblemDetails "detail" alanını okur.
            return Problem(detail: "Kullanıcı adı veya şifre hatalı.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(tokenService.CreateToken(request.Username));
    }
}