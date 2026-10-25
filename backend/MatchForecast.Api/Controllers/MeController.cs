using System.Globalization;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MatchForecast.Api.Controllers;

// Giriş yapmış kullanıcının kendi bilgileri. Fallback policy sayesinde token ister; her rol (misafir dahil) erişebilir.
[ApiController]
[Route("api/me")]
public sealed class MeController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        var userName = User.FindFirst(JwtTokenService.NameClaimType)?.Value ?? string.Empty;
        var roles = User.FindAll(JwtTokenService.RoleClaimType).Select(x => x.Value).ToList();

        // Görünen ad token'da yok; veritabanından okunur. Kullanıcı bulunamazsa kullanıcı adıyla devam edilir.
        string? displayName = null;
        if (int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            displayName = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.DisplayName).FirstOrDefaultAsync(ct);

        return new MeResponse(userName, displayName, roles);
    }
}