using System.Globalization;
using MatchForecast.Api.Services.Security.Guest;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Models.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MatchForecast.Api.Controllers;

/*
 * Giriş yapmış kullanıcının favori ligleri. Lig anahtarı frontend'deki filtreyle aynıdır: "Ülke / Lig".
 * Ekleme ve silme idempotenttir; her ikisi de güncel listeyi döner ki frontend ayrıca GET yapmasın.
 * Misafir hesabı paylaşılan bir hesap olduğu için kapalıdır.
 */
[ApiController]
[Route("api/me/favorite-leagues")]
[Authorize(Policy = GuestAccess.NotGuestPolicy)]
public sealed class FavoriteLeaguesController(AppDbContext db) : ControllerBase
{
    private const int MaxLeagueLength = 200;

    [HttpGet]
    public async Task<ActionResult<List<string>>> Get(CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized();

        return await ListAsync(userId, ct);
    }

    // Örnek: PUT /api/me/favorite-leagues?league=Turkey%20%2F%20S%C3%BCper%20Lig
    [HttpPut]
    public async Task<ActionResult<List<string>>> Add([FromQuery] string? league, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized();
        if (Validate(league) is { } problem)
            return problem;

        var key = league!.Trim();
        if (!await db.UserFavoriteLeagues.AnyAsync(x => x.UserId == userId && x.League == key, ct))
        {
            db.UserFavoriteLeagues.Add(new UserFavoriteLeague { UserId = userId, League = key });
            await db.SaveChangesAsync(ct);
        }

        return await ListAsync(userId, ct);
    }

    [HttpDelete]
    public async Task<ActionResult<List<string>>> Remove([FromQuery] string? league, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized();
        if (Validate(league) is { } problem)
            return problem;

        var key = league!.Trim();
        var existing = await db.UserFavoriteLeagues.FirstOrDefaultAsync(x => x.UserId == userId && x.League == key, ct);
        if (existing is not null)
        {
            db.UserFavoriteLeagues.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return await ListAsync(userId, ct);
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    private ObjectResult? Validate(string? league) =>
        string.IsNullOrWhiteSpace(league) || league.Trim().Length > MaxLeagueLength
            ? Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: Lang.T($"Lig adı boş olamaz ve en fazla {MaxLeagueLength} karakter olabilir.", $"League must not be empty and can be at most {MaxLeagueLength} characters."))
            : null;

    private Task<List<string>> ListAsync(int userId, CancellationToken ct) =>
        db.UserFavoriteLeagues.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.League)
            .Select(x => x.League)
            .ToListAsync(ct);
}