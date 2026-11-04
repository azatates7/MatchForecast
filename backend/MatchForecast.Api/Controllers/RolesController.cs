using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Controllers;

// Liste, kullanıcıya rol atarken de gerekir: users.view VEYA roles.manage yeterli. Rol CRUD'u adım 8.2'de.
[ApiController]
[Route("api/roles")]
[RequirePermission(PermissionKeys.UsersView, PermissionKeys.RolesManage)]
public sealed class RolesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RoleResponse>>> GetAll(CancellationToken ct) =>
        await db.Roles.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new RoleResponse(x.Id, x.Name, x.Description))
            .ToListAsync(ct);
}