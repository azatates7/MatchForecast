using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Services.Security.Guest;

/*
 * Misafir kullanıcısı. Guest:UserName / Guest:Email ayarları (.env / user-secrets) doluysa ve bu kullanıcı adıyla bir
 * kullanıcı yoksa açılışta bir kez oluşturulur ve yalnızca Guest rolü verilir. Kullanıcı varsa hiçbir şey yapmaz.
 * Giriş ekranındaki "Misafir olarak devam et" şifre istemez (POST /api/auth/guest). Guest:Password isteğe bağlıdır:
 * boşsa kullanıcı adı/şifre formundan misafir girişi yapılamaz, yalnızca buton çalışır.
 * E-posta gerçek olmamalı (ör. misafir@matchforecast.online): şifre sıfırlama maili kimseye gitmez, Google girişiyle çakışmaz.
 * "Guest" bölümü app_settings tablosuna taşınmaz; değerler yalnızca ortamdan okunur.
 */
public static class GuestUserBootstrapper
{
    public const string Section = "Guest";
    public const string DisplayName = "Misafir Kullanıcı";

    public static async Task EnsureGuestAsync(
        AppDbContext db, IPasswordHasher<User> hasher, IConfiguration configuration, ILogger logger, CancellationToken ct = default)
    {
        var section = configuration.GetSection(Section);
        var userName = section["UserName"]?.Trim().ToLowerInvariant();
        var email = section["Email"]?.Trim().ToLowerInvariant();
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email))
            return;

        if (await db.Users.AnyAsync(x => x.UserName == userName || x.Email == email, ct))
            return;

        var guestRole = await db.Roles.FirstAsync(x => x.Name == RoleNames.Guest, ct);

        var user = new User { UserName = userName, Email = email, DisplayName = DisplayName, EmailConfirmed = true };
        if (!string.IsNullOrWhiteSpace(password))
            user.PasswordHash = hasher.HashPassword(user, password);
        
        user.UserRoles.Add(new UserRole { RoleId = guestRole.Id });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Misafir kullanıcısı hazırlandı: {UserName}", userName);
    }
}