using System.Linq.Expressions;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Common;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Controllers;

/*
 * Roller. Liste kullanıcıya rol atarken de gerekir: users.view VEYA roles.manage yeterli; değişiklikler roles.manage ister.
 * Sistem rolleri (Admin, User, Guest) kodda adıyla kullanıldığı için yeniden adlandırılamaz ve silinemez.
 * Admin tüm yetkilere örtük olarak sahiptir; yetki listesi düzenlenemez. Yetki değişiklikleri kullanıcılara bir sonraki
 * token yenilemesinde yansır.
 */
[ApiController]
[Route("api/roles")]
[RequirePermission(PermissionKeys.UsersView, PermissionKeys.RolesManage)]
public sealed class RolesController(AppDbContext db) : ControllerBase
{
    private static readonly Expression<Func<Role, RoleResponse>> ToResponse = r => new RoleResponse(
        r.Id,
        r.Name,
        r.Description,
        r.IsSystem,
        r.Name == RoleNames.Admin,
        r.RolePermissions.Select(rp => rp.PermissionId).OrderBy(x => x).ToList(),
        r.UserRoles.Count,
        r.GroupRoles.Count);

    [HttpGet]
    public async Task<ActionResult<List<RoleResponse>>> GetAll(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(x => x.Name).Select(ToResponse).ToListAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoleResponse>> Get(int id, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking().Where(x => x.Id == id).Select(ToResponse).FirstOrDefaultAsync(ct);
        return role is null ? NotFound() : role;
    }

    [RequirePermission(PermissionKeys.RolesManage)]
    [HttpPost]
    public async Task<ActionResult<RoleResponse>> Create(RoleRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await NameTakenAsync(name, exceptId: null, ct))
            return DuplicateName();

        var role = new Role { Name = name, Description = request.Description };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);

        var response = await db.Roles.AsNoTracking().Where(x => x.Id == role.Id).Select(ToResponse).FirstAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, response);
    }

    [RequirePermission(PermissionKeys.RolesManage)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, RoleRequest request, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
            return NotFound();

        var name = request.Name.Trim();
        if (name != role.Name)
        {
            if (role.IsSystem)
                return Refuse(Lang.T("Sistem rollerinin adı değiştirilemez.", "System roles cannot be renamed."));
            if (await NameTakenAsync(name, exceptId: id, ct))
                return DuplicateName();
            role.Name = name;
        }

        role.Description = request.Description;
        role.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Kullanıcıya veya gruba atanmış bir rol yalnızca force=true ile silinir (atamalar da silinir).
    [RequirePermission(PermissionKeys.RolesManage)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force, CancellationToken ct)
    {
        // Atamalar yüklenir ki takip edilen kayıtlar da silinsin (DB'de FK cascade zaten var).
        var role = await db.Roles
            .Include(x => x.UserRoles)
            .Include(x => x.GroupRoles)
            .Include(x => x.RolePermissions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
            return NotFound();
        if (role.IsSystem)
            return Refuse(Lang.T("Sistem rolleri silinemez.", "System roles cannot be deleted."));

        if (!force && (role.UserRoles.Count > 0 || role.GroupRoles.Count > 0))
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: Lang.T(
                $"Bu rol {role.UserRoles.Count} kullanıcıya ve {role.GroupRoles.Count} gruba atanmış. Yine de silmek için onaylayın (force=true).",
                $"This role is assigned to {role.UserRoles.Count} users and {role.GroupRoles.Count} groups. Confirm to delete anyway (force=true)."));

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Rolün yetkilerini verilen listeyle değiştirir (listede olmayanlar kaldırılır, yeniler eklenir).</summary>
    [RequirePermission(PermissionKeys.RolesManage)]
    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> SetPermissions(int id, RolePermissionsRequest request, CancellationToken ct)
    {
        var role = await db.Roles.Include(x => x.RolePermissions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
            return NotFound();
        if (role.Name == RoleNames.Admin)
            return Refuse(Lang.T("Admin rolü tüm yetkilere sahiptir; yetkileri düzenlenemez.", "The Admin role has every permission; its permissions cannot be edited."));

        var permissionIds = request.PermissionIds.Distinct().ToList();
        if (await db.Permissions.CountAsync(x => permissionIds.Contains(x.Id), ct) != permissionIds.Count)
            return Refuse(Lang.T("Listede bulunmayan bir yetki id'si var.", "The list contains an unknown permission id."));

        // Mevcut atamalar korunur (AssignedAt değişmez); yalnızca fark uygulanır.
        role.RolePermissions.RemoveAll(x => !permissionIds.Contains(x.PermissionId));
        foreach (var permissionId in permissionIds.Where(pid => role.RolePermissions.All(x => x.PermissionId != pid)))
            role.RolePermissions.Add(new RolePermission { PermissionId = permissionId });

        role.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Unique index büyük/küçük harf duyarlı; "editor" ile "Editor" iki ayrı rol olmasın.
    private Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        return db.Roles.AnyAsync(x => x.Name.ToLower() == lower && x.Id != exceptId, ct);
    }

    private ObjectResult DuplicateName() =>
        Problem(statusCode: StatusCodes.Status409Conflict, detail: Lang.T("Bu adda bir rol zaten var.", "A role with this name already exists."));

    private ObjectResult Refuse(string detail) => Problem(statusCode: StatusCodes.Status400BadRequest, detail: detail);
}