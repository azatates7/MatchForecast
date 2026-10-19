using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Data.Seed;

// İleride [Authorize(Roles = RoleNames.Admin)] gibi kullanımlar için tek kaynak.
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string User = "User";
}

/*
 * Temel roller. Geçici değil; her Migrate() sonunda eksik olan rol (isme göre) eklenir, var olanlara dokunulmaz.
 * Yeni bir rol gerektiğinde CreateRoles listesine eklemek yeterli.
 */
public static class RoleSeed
{
    private static Role[] CreateRoles() =>
    [
        new() { Name = RoleNames.Admin, Description = "Tüm yönetim işlemleri" },
        new() { Name = RoleNames.User, Description = "Standart kullanıcı" }
    ];

    public static void Seed(DbContext context, bool storeManagementPerformed)
    {
        var db = (AppDbContext)context;
        var existing = db.Roles.Select(x => x.Name).ToHashSet();

        db.Roles.AddRange(CreateRoles().Where(x => !existing.Contains(x.Name)));
        db.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool storeManagementPerformed, CancellationToken ct)
    {
        var db = (AppDbContext)context;
        var existing = (await db.Roles.Select(x => x.Name).ToListAsync(ct)).ToHashSet();

        db.Roles.AddRange(CreateRoles().Where(x => !existing.Contains(x.Name)));
        await db.SaveChangesAsync(ct);
    }
}