using MatchForecast.Data.Configuration;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Services.Security;

/*
 * Kullanıcıların etkin yetkileri: doğrudan rollerinin ve üyesi oldukları grupların rollerinin yetkileri.
 * Admin (doğrudan veya grup üzerinden) tablodaki tüm yetkilere sahiptir. Token (AuthService) ve kullanıcı listesi
 * (UsersController) aynı kuralı buradan kullanır.
 */
public sealed class PermissionResolver(AppDbContext db)
{
    // Her kullanıcı için anahtara göre (ordinal) sıralı, tekrarsız yetki listesi; yetkisi olmayanlar boş liste alır.
    public async Task<Dictionary<int, List<string>>> ForUsersAsync(IReadOnlyCollection<int> userIds, CancellationToken ct)
    {
        var direct = await (
            from ur in db.UserRoles
            where userIds.Contains(ur.UserId)
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            select new { ur.UserId, rp.Permission.Key }).ToListAsync(ct);

        var viaGroups = await (
            from ug in db.UserGroups
            where userIds.Contains(ug.UserId)
            join gr in db.GroupRoles on ug.GroupId equals gr.GroupId
            join rp in db.RolePermissions on gr.RoleId equals rp.RoleId
            select new { ug.UserId, rp.Permission.Key }).ToListAsync(ct);

        var adminIds = (await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Where(AdminGuard.IsAdmin)
            .Select(u => u.Id)
            .ToListAsync(ct)).ToHashSet();
        var allKeys = adminIds.Count > 0 ? await db.Permissions.Select(p => p.Key).ToListAsync(ct) : [];

        var assigned = direct.Concat(viaGroups).ToLookup(x => x.UserId, x => x.Key);
        return userIds.Distinct().ToDictionary(
            id => id,
            id => (adminIds.Contains(id) ? allKeys : assigned[id]).Distinct().Order(StringComparer.Ordinal).ToList());
    }
}