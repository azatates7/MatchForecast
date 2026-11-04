using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Data.Seed;

// Sistem rolleri (Role.IsSystem): kodda adıyla kullanılır. Yetki kontrolleri için rol yerine PermissionKeys kullanın.
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Guest = "Guest";
}

/*
 * Sistem rolleri. Her Migrate() sonunda eksik olan rol (isme göre) eklenir; var olanların yalnızca IsSystem işareti
 * düzeltilir (açıklama vb. panelden değiştirilmiş olabilir, dokunulmaz). Panelden eklenen roller (IsSystem=false) bu
 * listede yer almaz.
 */
public static class RoleSeed
{
    private static Role[] CreateRoles() =>
    [
        new() { Name = RoleNames.Admin, Description = "Tüm yetkiler (sonradan eklenenler dahil)", IsSystem = true },
        new() { Name = RoleNames.User, Description = "Standart kullanıcı", IsSystem = true },
        new() { Name = RoleNames.Guest, Description = "Misafir: yalnızca bugünün Süper Lig maçları", IsSystem = true }
    ];

    private static readonly string[] SystemRoleNames = [RoleNames.Admin, RoleNames.User, RoleNames.Guest];

    public static void Seed(DbContext context, bool storeManagementPerformed)
    {
        var db = (AppDbContext)context;
        var existing = db.Roles.Where(x => SystemRoleNames.Contains(x.Name)).ToList();
        Apply(db, existing);
        db.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool storeManagementPerformed, CancellationToken ct)
    {
        var db = (AppDbContext)context;
        var existing = await db.Roles.Where(x => SystemRoleNames.Contains(x.Name)).ToListAsync(ct);
        Apply(db, existing);
        await db.SaveChangesAsync(ct);
    }

    // Eksik sistem rollerini ekler; IsSystem eklenmeden önce oluşmuş kayıtları işaretler.
    private static void Apply(AppDbContext db, List<Role> existing)
    {
        foreach (var role in existing.Where(x => !x.IsSystem))
            role.IsSystem = true;

        var names = existing.Select(x => x.Name).ToHashSet();
        db.Roles.AddRange(CreateRoles().Where(x => !names.Contains(x.Name)));
    }
}