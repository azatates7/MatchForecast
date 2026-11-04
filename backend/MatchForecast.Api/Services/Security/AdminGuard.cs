using System.Globalization;
using System.Linq.Expressions;
using System.Security.Claims;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MatchForecast.Api.Services.Security;

/*
 * Sistemi yöneticisiz bırakacak değişiklikleri yakalar (kullanıcı, rol ve grup uçları ortak kullanır).
 * "Admin" = Admin rolü doğrudan verilmiş VEYA Admin rolü veren bir grubun üyesi.
 * Çağıran, değişiklik sonrası Admin'liğini kaybedecek kullanıcıları hesaplar; CheckLossAsync karar verir:
 * - İsteği yapan kişi kaybedenler arasındaysa (kendi Admin'liğini kaldırıyor) → reddedilir.
 * - Değişiklikten sonra hiç aktif Admin kalmıyorsa → reddedilir.
 */
public sealed class AdminGuard(AppDbContext db)
{
    public static readonly Expression<Func<User, bool>> IsAdmin = u =>
        u.UserRoles.Any(r => r.Role.Name == RoleNames.Admin)
        || u.UserGroups.Any(g => g.Group.GroupRoles.Any(gr => gr.Role.Name == RoleNames.Admin));

    public static string SelfLossMessage =>
        Lang.T("Kendi Admin yetkinizi kaldıramazsınız.", "You cannot remove your own Admin access.");

    public static string LastAdminMessage =>
        Lang.T("Sistemde en az bir aktif Admin kalmalı.", "At least one active Admin must remain.");

    public Task<bool> IsAdminAsync(int userId, CancellationToken ct) =>
        db.Users.Where(x => x.Id == userId).AnyAsync(IsAdmin, ct);

    // groupIds içindeki gruplardan en az biri Admin rolü veriyor mu?
    public Task<bool> AnyGroupGrantsAdminAsync(IReadOnlyCollection<int> groupIds, CancellationToken ct) =>
        db.Groups.AnyAsync(x => groupIds.Contains(x.Id) && x.GroupRoles.Any(gr => gr.Role.Name == RoleNames.Admin), ct);

    /*
     * Admin'liği YALNIZCA bu gruptan gelen kullanıcılar (doğrudan Admin rolü veya Admin veren başka grubu olmayanlar).
     * Grup silinir / Admin rolünü kaybeder / üyeler çıkarılırsa bunlar Admin'liği kaybeder. among verilirse yalnızca onlar.
     */
    public async Task<List<int>> AdminsOnlyViaGroupAsync(int groupId, IReadOnlyCollection<int>? among, CancellationToken ct)
    {
        if (!await AnyGroupGrantsAdminAsync([groupId], ct))
            return [];

        var query = db.Users.Where(u =>
            u.UserGroups.Any(ug => ug.GroupId == groupId)
            && !u.UserRoles.Any(r => r.Role.Name == RoleNames.Admin)
            && !u.UserGroups.Any(ug => ug.GroupId != groupId && ug.Group.GroupRoles.Any(gr => gr.Role.Name == RoleNames.Admin)));
        if (among is not null)
            query = query.Where(u => among.Contains(u.Id));

        return await query.Select(u => u.Id).ToListAsync(ct);
    }

    // null: değişiklik yapılabilir; değilse kullanıcıya gösterilecek hata metni (400).
    public async Task<string?> CheckLossAsync(IReadOnlyCollection<int> losing, int? currentUserId, CancellationToken ct)
    {
        if (losing.Count == 0)
            return null;
        if (currentUserId is { } me && losing.Contains(me))
            return SelfLossMessage;

        var anyAdminRemains = await db.Users
            .Where(u => u.IsActive && !losing.Contains(u.Id))
            .AnyAsync(IsAdmin, ct);
        return anyAdminRemains ? null : LastAdminMessage;
    }
}

public static class ClaimsPrincipalExtensions
{
    // İsteği yapan kullanıcının id'si (JWT "sub"); yoksa null.
    public static int? GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}