using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Controllers;

// Liste, kullanıcıya grup atarken de gerekir: users.view veya groups.manage yeterli. Grup CRUD'u adım 8.2'de.
[ApiController]
[Route("api/groups")]
[RequirePermission(PermissionKeys.UsersView, PermissionKeys.GroupsManage)]
public sealed class GroupsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<GroupResponse>>> GetAll(CancellationToken ct) =>
        await db.Groups.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new GroupResponse(x.Id, x.Name, x.Description))
            .ToListAsync(ct);
}