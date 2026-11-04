using MatchForecast.Models.Common;
using System.Globalization;
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
using Microsoft.IdentityModel.JsonWebTokens;

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
        return CreatedAtAction(nameof(Get), new { id = user.Id }, response);
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
            if (id == CurrentUserId())
                return Refuse(Lang.T("Kendi hesabınızı pasife alamazsınız.", "You cannot deactivate your own account."));
            if (await IsLastActiveAdminAsync(id, ct))
                return LastAdmin();
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
        if (id == CurrentUserId())
            return Refuse(Lang.T("Kendi hesabınızı silemezsiniz.", "You cannot delete your own account."));
        if (await IsLastActiveAdminAsync(id, ct))
            return LastAdmin();

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
        var adminAfter = roleIds.Contains(adminRoleId) || await IsAdminViaGroupsAsync(id, groupIds: null, ct);
        if (await GuardAdminLossAsync(id, adminAfter, ct) is { } refused)
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
                         || await IsAdminViaGroupsAsync(id, groupIds, ct);
        if (await GuardAdminLossAsync(id, adminAfter, ct) is { } refused)
            return refused;

        user.UserGroups.RemoveAll(x => !groupIds.Contains(x.GroupId));
        foreach (var groupId in groupIds.Where(gid => user.UserGroups.All(x => x.GroupId != gid)))
            user.UserGroups.Add(new UserGroup { GroupId = groupId });

        user.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // İsteği yapan Admin'in kullanıcı id'si (JWT "sub").
    private int? CurrentUserId() =>
        int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    // Admin = Admin rolü doğrudan verilmiş VEYA Admin rolü veren bir grubun üyesi.
    private static readonly Expression<Func<User, bool>> IsAdmin = u =>
        u.UserRoles.Any(r => r.Role.Name == RoleNames.Admin)
        || u.UserGroups.Any(g => g.Group.GroupRoles.Any(gr => gr.Role.Name == RoleNames.Admin));

    // groupIds null ise kullanıcının mevcut grupları, değilse (SetGroups) atanacak gruplar değerlendirilir.
    private Task<bool> IsAdminViaGroupsAsync(int userId, IReadOnlyCollection<int>? groupIds, CancellationToken ct) =>
        groupIds is null
            ? db.UserGroups.AnyAsync(x => x.UserId == userId && x.Group.GroupRoles.Any(gr => gr.Role.Name == RoleNames.Admin), ct)
            : db.Groups.AnyAsync(x => groupIds.Contains(x.Id) && x.GroupRoles.Any(gr => gr.Role.Name == RoleNames.Admin), ct);

    // Değişiklik kullanıcının Admin'liğini kaldırıyorsa: kendi hesabıysa veya son aktif Admin ise reddedilir.
    private async Task<IActionResult?> GuardAdminLossAsync(int userId, bool adminAfter, CancellationToken ct)
    {
        if (adminAfter || !await db.Users.Where(x => x.Id == userId).AnyAsync(IsAdmin, ct))
            return null;
        if (userId == CurrentUserId())
            return Refuse(Lang.T("Kendi Admin yetkinizi kaldıramazsınız.", "You cannot remove your own Admin access."));
        if (await IsLastActiveAdminAsync(userId, ct))
            return LastAdmin();
        return null;
    }

    // Kullanıcı, aktif Admin'lerin (doğrudan veya grup üzerinden) tek kalanı mı?
    private async Task<bool> IsLastActiveAdminAsync(int userId, CancellationToken ct)
    {
        var activeAdminIds = await db.Users
            .Where(x => x.IsActive)
            .Where(IsAdmin)
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync(ct);
        return activeAdminIds.Count == 1 && activeAdminIds[0] == userId;
    }

    private ObjectResult Refuse(string detail) => Problem(statusCode: StatusCodes.Status400BadRequest, detail: detail);

    private ObjectResult LastAdmin() =>
        Refuse(Lang.T("Sistemde en az bir aktif Admin kalmalı.", "At least one active Admin must remain."));

    // Unique index büyük/küçük harf duyarlı; "Azat@x.com" ile "azat@x.com" iki ayrı kayıt olmasın.
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}