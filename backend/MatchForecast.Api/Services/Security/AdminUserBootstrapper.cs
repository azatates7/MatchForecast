using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Services.Security;

/*
 * İlk admin kullanıcısı. Token artık kullanıcı adı/şifre istediği ve /api/users yalnızca Admin'e açık olduğu için,
 * ilk kullanıcıyı oluşturacak birinin olması gerekir. Admin rolünde hiç kullanıcı yoksa ve Admin:UserName / Admin:Email /
 * Admin:Password ayarları (user-secrets / .env) doluysa açılışta bir kez oluşturulur. Admin varsa hiçbir şey yapmaz.
 * Şifre yalnızca ilk oluşturmada kullanılır; sonradan ayarı değiştirmek mevcut şifreyi değiştirmez.
 */
public static class AdminUserBootstrapper
{
    public const string Section = "Admin";

    public static async Task EnsureAdminAsync(
        AppDbContext db, IPasswordHasher<User> hasher, IConfiguration configuration, ILogger logger, CancellationToken ct = default)
    {
        var section = configuration.GetSection(Section);
        var userName = section["UserName"]?.Trim().ToLowerInvariant();
        var email = section["Email"]?.Trim().ToLowerInvariant();
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        if (await db.UserRoles.AnyAsync(x => x.Role.Name == RoleNames.Admin, ct))
            return;

        var adminRole = await db.Roles.FirstAsync(x => x.Name == RoleNames.Admin, ct);

        // Aynı kullanıcı adıyla (admin olmayan) bir kullanıcı varsa yenisi açılmaz, ona Admin rolü verilir.
        var user = await db.Users.FirstOrDefaultAsync(x => x.UserName == userName, ct);
        if (user is null)
        {
            user = new User { UserName = userName, Email = email, DisplayName = "Administrator", EmailConfirmed = true };
            db.Users.Add(user);
        }

        user.PasswordHash ??= hasher.HashPassword(user, password);
        user.UserRoles.Add(new UserRole { RoleId = adminRole.Id });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("İlk admin kullanıcısı hazırlandı: {UserName}", userName);
    }
}