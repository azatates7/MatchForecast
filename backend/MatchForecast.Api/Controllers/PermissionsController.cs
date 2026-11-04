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
 * Yetki tanımları. Liste rol düzenlerken de gerekir: permissions.manage VEYA roles.manage yeterli.
 * Değişiklikler permissions.manage ister. Sistem yetkileri (IsSystem, seed ile gelir) kod tarafından kontrol edildiği
 * için anahtarı değiştirilemez ve silinemez; adı/açıklaması değiştirilebilir.
 */
[ApiController]
[Route("api/permissions")]
[RequirePermission(PermissionKeys.PermissionsManage, PermissionKeys.RolesManage)]
public sealed class PermissionsController(AppDbContext db) : ControllerBase
{
    private static readonly Expression<Func<Permission, PermissionResponse>> ToResponse = p => new PermissionResponse(
        p.Id,
        p.Key,
        p.Name,
        p.NameEn,
        p.Description,
        p.IsSystem,
        p.RolePermissions.Select(rp => rp.RoleId).OrderBy(x => x).ToList());

    [HttpGet]
    public async Task<ActionResult<List<PermissionResponse>>> GetAll(CancellationToken ct) =>
        await db.Permissions.AsNoTracking().OrderBy(x => x.Key).Select(ToResponse).ToListAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PermissionResponse>> Get(int id, CancellationToken ct)
    {
        var permission = await db.Permissions.AsNoTracking().Where(x => x.Id == id).Select(ToResponse).FirstOrDefaultAsync(ct);
        return permission is null ? NotFound() : permission;
    }

    [RequirePermission(PermissionKeys.PermissionsManage)]
    [HttpPost]
    public async Task<ActionResult<PermissionResponse>> Create(CreatePermissionRequest request, CancellationToken ct)
    {
        var key = Normalize(request.Key);
        if (await db.Permissions.AnyAsync(x => x.Key == key, ct))
            return DuplicateKey();

        var permission = new Permission
        {
            Key = key,
            Name = request.Name.Trim(),
            NameEn = request.NameEn.Trim(),
            Description = request.Description
        };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync(ct);

        var response = await db.Permissions.AsNoTracking().Where(x => x.Id == permission.Id).Select(ToResponse).FirstAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = permission.Id }, response);
    }

    [RequirePermission(PermissionKeys.PermissionsManage)]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdatePermissionRequest request, CancellationToken ct)
    {
        var permission = await db.Permissions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (permission is null)
            return NotFound();

        if (request.Key is not null && Normalize(request.Key) is var key && key != permission.Key)
        {
            if (permission.IsSystem)
                return Problem(statusCode: StatusCodes.Status400BadRequest, detail: Lang.T(
                    "Sistem yetkilerinin anahtarı değiştirilemez; kod bu anahtarı kontrol ediyor.",
                    "The key of a system permission cannot be changed; the code checks this key."));
            if (await db.Permissions.AnyAsync(x => x.Key == key, ct))
                return DuplicateKey();
            permission.Key = key;
        }

        permission.Name = request.Name.Trim();
        permission.NameEn = request.NameEn.Trim();
        permission.Description = request.Description;
        permission.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Rollere yapılmış atamaları da (cascade) silinir; token'larda bir sonraki yenilemeye kadar kalır.
    [RequirePermission(PermissionKeys.PermissionsManage)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        // Atamalar yüklenir ki takip edilen kayıtlar da silinsin (DB'de FK cascade zaten var).
        var permission = await db.Permissions.Include(x => x.RolePermissions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (permission is null)
            return NotFound();
        if (permission.IsSystem)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: Lang.T(
                "Sistem yetkileri silinemez.", "System permissions cannot be deleted."));

        db.Permissions.Remove(permission);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ObjectResult DuplicateKey() =>
        Problem(statusCode: StatusCodes.Status409Conflict, detail: Lang.T("Bu yetki anahtarı zaten tanımlı.", "This permission key already exists."));

    // Unique index büyük/küçük harf duyarlı; anahtarlar küçük harfle saklanır.
    private static string Normalize(string key) => key.Trim().ToLowerInvariant();
}