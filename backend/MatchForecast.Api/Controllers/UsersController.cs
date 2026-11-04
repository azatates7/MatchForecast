using MatchForecast.Models.Common;
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

/*
 * Kullanıcı yönetimi: görüntülemek users.view, değiştirmek ayrıca users.manage ister (Admin her ikisine de sahiptir).
 * İlk admin, açılışta AdminUserBootstrapper ile oluşturulur.
 * Kilitlenme koruması: kimse kendi hesabını pasife alamaz/silemez, kendi Admin'liğini (doğrudan rol veya Admin rolü
 * veren grup) kaldıramaz; son aktif Admin de pasife alınamaz, silinemez ve Admin'liği kaldırılamaz.
 */
[ApiController]
[Route("api/users")]
[RequirePermission(PermissionKeys.UsersView)]
public sealed class UsersController(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    AuthService auth,
    AdminGuard adminGuard,
    PermissionResolver permissionResolver) : ControllerBase
{
    // Tek sorguda kullanıcı + rol adları + grup adları; entity (PasswordHash dahil) hiçbir zaman dışarı çıkmaz.
    // Etkin yetkiler ayrı hesaplanır (WithPermissionsAsync), burada boş gelir.
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
        u.UserGroups.Select(x => x.Group.Name).OrderBy(x => x).ToList(),
        Array.Empty<string>());

    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetAll(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(ToResponse).ToListAsync(ct);
        return await WithPermissionsAsync(users, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserResponse>> Get(int id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(ToResponse).FirstOrDefaultAsync(ct);
        if (user is null)
            return NotFound();

        return (await WithPermissionsAsync([user], ct))[0];
    }

    [RequirePermission(PermissionKeys.UsersManage)]
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var userName = Normalize(request.UserName);
        var email = Normalize(request.Email);

        if (await db.Users.AnyAsync(x => x.UserName == userName, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: Lang.T("Bu kullanıcı adı kullanılıyor.", "This user name is already taken."));

        if (await db.Users.AnyAsync(x => x.Email == email, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: Lang.T("Bu e-posta adresi kullanılıyor.", "This e-mail address is already in use."));

        var user = new User { UserName = userName, Email = email, DisplayName = request.DisplayName };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct); // Id ve CreatedAt veritabanından geri okunur.

        var response = await db.Users.AsNoTracking().Where(x => x.Id == user.Id).Select(ToResponse).FirstAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, response); // Yeni kullanıcının rolü/yetkisi yok.
    }

    [RequirePermission(PermissionKeys.UsersManage)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return NotFound();

        if (user.IsActive && !request.IsActive)
        {
            if (id == User.GetUserId())
                return Refuse(Lang.T("Kendi hesabınızı pasife alamazsınız.", "You cannot deactivate your own account."));
            if (await CheckAdminLossAsync(id, ct) is { } refused)
                return refused;
        }

        var email = Normalize(request.Email);
        if (email != user.Email)
        {
            if (await db.Users.AnyAsync(x => x.Email == email, ct))
                return Problem(statusCode: StatusCodes.Status409Conflict, detail: Lang.T("Bu e-posta adresi kullanılıyor.", "This e-mail address is already in use."));

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
    [RequirePermission(PermissionKeys.UsersManage)]
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
    [RequirePermission(PermissionKeys.UsersManage)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id == User.GetUserId())
            return Refuse(Lang.T("Kendi hesabınızı silemezsiniz.", "You cannot delete your own account."));
        if (await CheckAdminLossAsync(id, ct) is { } refused)
            return refused;

        var deleted = await db.Users.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }

    /// <summary>Kullanıcının rollerini verilen listeyle değiştirir (listede olmayanlar kaldırılır, yeniler eklenir).</summary>
    [RequirePermission(PermissionKeys.UsersManage)]
    [HttpPut("{id:int}/roles")]
    public async Task<IActionResult> SetRoles(int id, UserRolesRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(x => x.UserRoles).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return NotFound();

        var roleIds = request.RoleIds.Distinct().ToList();
        if (await db.Roles.CountAsync(x => roleIds.Contains(x.Id), ct) != roleIds.Count)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: Lang.T("Listede bulunmayan bir rol id'si var.", "The list contains an unknown role id."));

        var adminRoleId = await db.Roles.Where(x => x.Name == RoleNames.Admin).Select(x => x.Id).FirstOrDefaultAsync(ct);
        var adminViaGroups = await db.UserGroups
            .Where(x => x.UserId == id)
            .AnyAsync(x => x.Group.GroupRoles.Any(gr => gr.Role.Name == RoleNames.Admin), ct);
        if (!roleIds.Contains(adminRoleId) && !adminViaGroups && await CheckAdminLossAsync(id, ct) is { } refused)
            return refused;

        // Mevcut atamalar korunur (AssignedAt değişmez); yalnızca fark uygulanır.
        user.UserRoles.RemoveAll(x => !roleIds.Contains(x.RoleId));
        foreach (var roleId in roleIds.Where(rid => user.UserRoles.All(x => x.RoleId != rid)))
            user.UserRoles.Add(new UserRole { RoleId = roleId });

        user.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Kullanıcının gruplarını verilen listeyle değiştirir (listede olmayanlardan çıkarılır, yenilere eklenir).</summary>
    [RequirePermission(PermissionKeys.UsersManage)]
    [HttpPut("{id:int}/groups")]
    public async Task<IActionResult> SetGroups(int id, UserGroupsRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(x => x.UserGroups).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return NotFound();

        var groupIds = request.GroupIds.Distinct().ToList();
        if (await db.Groups.CountAsync(x => groupIds.Contains(x.Id), ct) != groupIds.Count)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: Lang.T("Listede bulunmayan bir grup id'si var.", "The list contains an unknown group id."));

        // Admin'liği yalnızca bir gruptan geliyorsa o gruptan çıkarmak da Admin'liği kaldırır.
        var adminAfter = await db.UserRoles.AnyAsync(x => x.UserId == id && x.Role.Name == RoleNames.Admin, ct)
                         || await adminGuard.AnyGroupGrantsAdminAsync(groupIds, ct);
        if (!adminAfter && await CheckAdminLossAsync(id, ct) is { } refused)
            return refused;

        user.UserGroups.RemoveAll(x => !groupIds.Contains(x.GroupId));
        foreach (var groupId in groupIds.Where(gid => user.UserGroups.All(x => x.GroupId != gid)))
            user.UserGroups.Add(new UserGroup { GroupId = groupId });

        user.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<List<UserResponse>> WithPermissionsAsync(List<UserResponse> users, CancellationToken ct)
    {
        var permissions = await permissionResolver.ForUsersAsync(users.Select(x => x.Id).ToList(), ct);
        return users.Select(x => x with { Permissions = permissions[x.Id] }).ToList();
    }

    // Kullanıcı Admin ise ve bu değişiklikle Admin'liğini kaybedecekse: kendisi veya son aktif Admin ise reddedilir.
    private async Task<IActionResult?> CheckAdminLossAsync(int userId, CancellationToken ct)
    {
        if (!await adminGuard.IsAdminAsync(userId, ct))
            return null;
        return await adminGuard.CheckLossAsync([userId], User.GetUserId(), ct) is { } error ? Refuse(error) : null;
    }

    private ObjectResult Refuse(string detail) => Problem(statusCode: StatusCodes.Status400BadRequest, detail: detail);

    // Unique index büyük/küçük harf duyarlı; "Azat@x.com" ile "azat@x.com" iki ayrı kayıt olmasın.
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}