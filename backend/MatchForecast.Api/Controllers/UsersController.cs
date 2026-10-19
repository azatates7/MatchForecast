using System.Linq.Expressions;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Controllers;

// Kullanıcı yönetimi yalnızca Admin rolüne açık. İlk admin, açılışta AdminUserBootstrapper ile oluşturulur.
[ApiController]
[Route("api/users")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class UsersController(AppDbContext db, IPasswordHasher<User> hasher, AuthService auth) : ControllerBase
{
    // Tek sorguda kullanıcı + rol adları + grup adları; entity (PasswordHash dahil) hiçbir zaman dışarı çıkmaz.
    private static readonly Expression<Func<User, UserResponse>> ToResponse = u => new UserResponse(
        u.Id,
        u.UserName,
        u.Email,
        u.DisplayName,
        u.IsActive,
        u.EmailConfirmed,
        u.CreatedAt,
        u.UpdatedAt,
        u.LastLoginAt,
        u.UserRoles.Select(x => x.Role.Name).OrderBy(x => x).ToList(),
        u.UserGroups.Select(x => x.Group.Name).OrderBy(x => x).ToList());

    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetAll(CancellationToken ct)
    {
        return await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(ToResponse).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserResponse>> Get(int id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(ToResponse).FirstOrDefaultAsync(ct);
        if (user is null)
            return NotFound();

        return user;
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var userName = Normalize(request.UserName);
        var email = Normalize(request.Email);

        if (await db.Users.AnyAsync(x => x.UserName == userName, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Bu kullanıcı adı kullanılıyor.");

        if (await db.Users.AnyAsync(x => x.Email == email, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Bu e-posta adresi kullanılıyor.");

        var user = new User { UserName = userName, Email = email, DisplayName = request.DisplayName };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct); // Id ve CreatedAt veritabanından geri okunur.

        var response = await db.Users.AsNoTracking().Where(x => x.Id == user.Id).Select(ToResponse).FirstAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, response);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return NotFound();

        var email = Normalize(request.Email);
        if (email != user.Email)
        {
            if (await db.Users.AnyAsync(x => x.Email == email, ct))
                return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Bu e-posta adresi kullanılıyor.");

            user.Email = email;
            user.EmailConfirmed = false; // Yeni adres henüz doğrulanmadı.
        }

        user.DisplayName = request.DisplayName;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);

        // Pasife alınan kullanıcı refresh token ile oturumunu uzatamasın.
        if (!user.IsActive)
            await auth.RevokeAllAsync(user.Id, ct);

        return NoContent();
    }

    /// <summary>Kullanıcının şifresini belirler/sıfırlar ve tüm açık oturumlarını (refresh token'larını) kapatır.</summary>
    [HttpPut("{id:int}/password")]
    public async Task<IActionResult> SetPassword(int id, SetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return NotFound();

        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        await auth.RevokeAllAsync(user.Id, ct);
        return NoContent();
    }

    // Rol/grup atamaları, harici login hesapları ve refresh token'lar DB'de cascade ile birlikte silinir.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await db.Users.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }

    /// <summary>Kullanıcının rollerini verilen listeyle değiştirir (listede olmayanlar kaldırılır, yeniler eklenir).</summary>
    [HttpPut("{id:int}/roles")]
    public async Task<IActionResult> SetRoles(int id, UserRolesRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(x => x.UserRoles).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return NotFound();

        var roleIds = request.RoleIds.Distinct().ToList();
        if (await db.Roles.CountAsync(x => roleIds.Contains(x.Id), ct) != roleIds.Count)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Listede bulunmayan bir rol id'si var.");

        // Mevcut atamalar korunur (AssignedAt değişmez); yalnızca fark uygulanır.
        user.UserRoles.RemoveAll(x => !roleIds.Contains(x.RoleId));
        foreach (var roleId in roleIds.Where(rid => user.UserRoles.All(x => x.RoleId != rid)))
            user.UserRoles.Add(new UserRole { RoleId = roleId });

        user.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Kullanıcının gruplarını verilen listeyle değiştirir (listede olmayanlardan çıkarılır, yenilere eklenir).</summary>
    [HttpPut("{id:int}/groups")]
    public async Task<IActionResult> SetGroups(int id, UserGroupsRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(x => x.UserGroups).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return NotFound();

        var groupIds = request.GroupIds.Distinct().ToList();
        if (await db.Groups.CountAsync(x => groupIds.Contains(x.Id), ct) != groupIds.Count)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Listede bulunmayan bir grup id'si var.");

        user.UserGroups.RemoveAll(x => !groupIds.Contains(x.GroupId));
        foreach (var groupId in groupIds.Where(gid => user.UserGroups.All(x => x.GroupId != gid)))
            user.UserGroups.Add(new UserGroup { GroupId = groupId });

        user.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Unique index büyük/küçük harf duyarlı; "Azat@x.com" ile "azat@x.com" iki ayrı kayıt olmasın.
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}