using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Data.Seed;

/*
 * Kodun kontrol ettiği (sistem) yetkilerin anahtarları. Yeni bir yetki kontrolü eklenince:
 * 1. Buraya sabit, PermissionSeed.CreatePermissions listesine TR/EN adıyla satır eklenir.
 * 2. Endpoint'e [RequirePermission(PermissionKeys.X)] yazılır. Program.cs'e dokunmak gerekmez.
 * Admin rolü tüm yetkilere örtük olarak sahiptir. Panelden eklenen özel yetkiler bu listede yer almaz.
 */
public static class PermissionKeys
{
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string PermissionsManage = "permissions.manage";
    public const string GroupsManage = "groups.manage";
    public const string CacheClear = "cache.clear";
    public const string ForecastRefresh = "forecast.refresh";
    public const string AppSettingsManage = "app-settings.manage";
}

/*
 * Sistem yetkileri: her Migrate() sonunda eksik olan (anahtara göre) eklenir; var olanın yalnızca IsSystem işareti
 * düzeltilir (ad/açıklama panelden değiştirilmiş olabilir). Rol-yetki atamalarına hiç dokunulmaz: varsayılan olarak
 * yalnızca Admin (örtük olarak) yetkilidir, diğer roller panelden yetkilendirilir.
 */
public static class PermissionSeed
{
    private static Permission[] CreatePermissions() =>
    [
        Define(PermissionKeys.UsersView, "Kullanıcıları görüntüleme", "View users", "Kullanıcı listesini ve ayrıntılarını görür."),
        Define(PermissionKeys.UsersManage, "Kullanıcıları yönetme", "Manage users", "Kullanıcı oluşturur, düzenler, siler; şifre, rol ve grup atar."),
        Define(PermissionKeys.RolesManage, "Rolleri yönetme", "Manage roles", "Rol oluşturur, düzenler, siler ve rollere yetki atar."),
        Define(PermissionKeys.PermissionsManage, "Yetki tanımlarını yönetme", "Manage permissions", "Özel yetki tanımlarını ekler, düzenler, siler."),
        Define(PermissionKeys.GroupsManage, "Grupları yönetme", "Manage groups", "Grup oluşturur, düzenler, siler; gruba rol ve üye atar."),
        Define(PermissionKeys.CacheClear, "Önbelleği silme", "Clear cache", "Maç ekranındaki Önbelleği Sil işlemi."),
        Define(PermissionKeys.ForecastRefresh, "Tahmini yeniden üretme", "Regenerate forecasts", "Tahmin isteklerinde refresh=true ile önbelleği atlar."),
        Define(PermissionKeys.AppSettingsManage, "Uygulama ayarlarını yönetme", "Manage app settings", "/api/app-settings uçları (yalnızca API).")
    ];

    private static Permission Define(string key, string name, string nameEn, string description) =>
        new() { Key = key, Name = name, NameEn = nameEn, Description = description, IsSystem = true };

    public static void Seed(DbContext context, bool storeManagementPerformed)
    {
        var db = (AppDbContext)context;
        var existing = db.Permissions.ToList();
        Apply(db, existing);
        db.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool storeManagementPerformed, CancellationToken ct)
    {
        var db = (AppDbContext)context;
        var existing = await db.Permissions.ToListAsync(ct);
        Apply(db, existing);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(AppDbContext db, List<Permission> existing)
    {
        var byKey = existing.ToDictionary(x => x.Key);
        foreach (var permission in CreatePermissions())
        {
            if (byKey.TryGetValue(permission.Key, out var current))
                current.IsSystem = true; // Aynı anahtarla panelden eklenmiş olabilir; artık kod kontrol ediyor.
            else
                db.Permissions.Add(permission);
        }
    }
}