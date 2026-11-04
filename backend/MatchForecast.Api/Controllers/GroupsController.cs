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
 * Gruplar: kullanıcılara toplu rol vermek için (gruba verilen roller her üyeye geçer; token'a yansır).
 * Liste kullanıcıya grup atarken de gerekir: users.view VEYA groups.manage yeterli; değişiklikler groups.manage ister.
 * Admin rolü veren bir grupta silme / Admin rolünü kaldırma / üye çıkarma, Admin'liği yalnızca bu gruptan gelen
 * kullanıcıları etkiler: isteği yapan kişi bunlardan biriyse veya hiç aktif Admin kalmayacaksa reddedilir (AdminGuard).
 */
[ApiController]
[Route("api/groups")]
[RequirePermission(PermissionKeys.UsersView, PermissionKeys.GroupsManage)]
public sealed class GroupsController(AppDbContext db, AdminGuard adminGuard) : ControllerBase
{
    private static readonly Expression<Func<Group, GroupResponse>> ToResponse = g => new GroupResponse(
        g.Id,
        g.Name,
        g.Description,
        g.GroupRoles.Select(x => x.RoleId).OrderBy(x => x).ToList(),
        g.UserGroups.Select(x => x.UserId).OrderBy(x => x).ToList());

    [HttpGet]
    public async Task<ActionResult<List<GroupResponse>>> GetAll(CancellationToken ct) =>
        await db.Groups.AsNoTracking().OrderBy(x => x.Name).Select(ToResponse).ToListAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GroupResponse>> Get(int id, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking().Where(x => x.Id == id).Select(ToResponse).FirstOrDefaultAsync(ct);
        return group is null ? NotFound() : group;
    }

    [RequirePermission(PermissionKeys.GroupsManage)]
    [HttpPost]
    public async Task<ActionResult<GroupResponse>> Create(GroupRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await NameTakenAsync(name, exceptId: null, ct))
            return DuplicateName();

        var group = new Group { Name = name, Description = request.Description };
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);

        var response = await db.Groups.AsNoTracking().Where(x => x.Id == group.Id).Select(ToResponse).FirstAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = group.Id }, response);
    }

    [RequirePermission(PermissionKeys.GroupsManage)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, GroupRequest request, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (group is null)
            return NotFound();

        var name = request.Name.Trim();
        if (name != group.Name && await NameTakenAsync(name, exceptId: id, ct))
            return DuplicateName();

        group.Name = name;
        group.Description = request.Description;
        group.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Üyelikler ve rol atamaları da silinir; kullanıcıların kendisi silinmez.
    [RequirePermission(PermissionKeys.GroupsManage)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        // Atamalar yüklenir ki takip edilen kayıtlar da silinsin (DB'de FK cascade zaten var).
        var group = await db.Groups
            .Include(x => x.UserGroups)
            .Include(x => x.GroupRoles)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (group is null)
            return NotFound();

        var losing = await adminGuard.AdminsOnlyViaGroupAsync(id, among: null, ct);
        if (await adminGuard.CheckLossAsync(losing, User.GetUserId(), ct) is { } error)
            return Refuse(error);

        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Grubun rollerini verilen listeyle değiştirir (listede olmayanlar kaldırılır, yeniler eklenir).</summary>
    [RequirePermission(PermissionKeys.GroupsManage)]
    [HttpPut("{id:int}/roles")]
    public async Task<IActionResult> SetRoles(int id, GroupRolesRequest request, CancellationToken ct)
    {
        var group = await db.Groups.Include(x => x.GroupRoles).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (group is null)
            return NotFound();

        var roleIds = request.RoleIds.Distinct().ToList();
        if (await db.Roles.CountAsync(x => roleIds.Contains(x.Id), ct) != roleIds.Count)
            return Refuse(Lang.T("Listede bulunmayan bir rol id'si var.", "The list contains an unknown role id."));

        // Grup Admin rolünü kaybediyorsa, Admin'liği yalnızca bu gruptan gelen üyeler etkilenir.
        var adminRoleId = await db.Roles.Where(x => x.Name == RoleNames.Admin).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (!roleIds.Contains(adminRoleId))
        {
            var losing = await adminGuard.AdminsOnlyViaGroupAsync(id, among: null, ct);
            if (await adminGuard.CheckLossAsync(losing, User.GetUserId(), ct) is { } error)
                return Refuse(error);
        }

        // Mevcut atamalar korunur (AssignedAt değişmez); yalnızca fark uygulanır.
        group.GroupRoles.RemoveAll(x => !roleIds.Contains(x.RoleId));
        foreach (var roleId in roleIds.Where(rid => group.GroupRoles.All(x => x.RoleId != rid)))
            group.GroupRoles.Add(new GroupRole { RoleId = roleId });

        group.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Grubun üyelerini verilen listeyle değiştirir (listede olmayanlar çıkarılır, yeniler eklenir).</summary>
    [RequirePermission(PermissionKeys.GroupsManage)]
    [HttpPut("{id:int}/users")]
    public async Task<IActionResult> SetUsers(int id, GroupUsersRequest request, CancellationToken ct)
    {
        var group = await db.Groups.Include(x => x.UserGroups).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (group is null)
            return NotFound();

        var userIds = request.UserIds.Distinct().ToList();
        if (await db.Users.CountAsync(x => userIds.Contains(x.Id), ct) != userIds.Count)
            return Refuse(Lang.T("Listede bulunmayan bir kullanıcı id'si var.", "The list contains an unknown user id."));

        // Çıkarılan üyelerden Admin'liği yalnızca bu gruptan gelenler etkilenir (grup Admin vermiyorsa liste boştur).
        var removed = group.UserGroups.Select(x => x.UserId).Where(uid => !userIds.Contains(uid)).ToList();
        if (removed.Count > 0)
        {
            var losing = await adminGuard.AdminsOnlyViaGroupAsync(id, removed, ct);
            if (await adminGuard.CheckLossAsync(losing, User.GetUserId(), ct) is { } error)
                return Refuse(error);
        }

        group.UserGroups.RemoveAll(x => !userIds.Contains(x.UserId));
        foreach (var userId in userIds.Where(uid => group.UserGroups.All(x => x.UserId != uid)))
            group.UserGroups.Add(new UserGroup { UserId = userId });

        group.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Unique index büyük/küçük harf duyarlı; "Ekip" ile "ekip" iki ayrı grup olmasın.
    private Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        return db.Groups.AnyAsync(x => x.Name.ToLower() == lower && x.Id != exceptId, ct);
    }

    private ObjectResult DuplicateName() =>
        Problem(statusCode: StatusCodes.Status409Conflict, detail: Lang.T("Bu adda bir grup zaten var.", "A group with this name already exists."));

    private ObjectResult Refuse(string detail) => Problem(statusCode: StatusCodes.Status400BadRequest, detail: detail);
}