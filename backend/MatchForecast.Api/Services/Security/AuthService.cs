using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MatchForecast.Api.Options;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Security;

/*
 * Kullanıcı adı/şifre ile giriş, refresh token rotasyonu ve iptali.
 * - Refresh token 64 byte kriptografik rastgele değerdir; DB'de yalnızca SHA-256 hash'i tutulur (DB sızsa da token'lar kullanılamaz).
 * - Her yenilemede eski token iptal edilir, yenisi verilir (rotation). İptal edilmiş bir token tekrar gelirse token çalınmış
 *   olabilir: kullanıcının tüm aktif refresh token'ları iptal edilir ve yeniden giriş gerekir (reuse detection).
 * - Başarısız durumlarda null döner; çağıran tek tip 401 verir, sebebi (kullanıcı yok / şifre yanlış / pasif) dışarı sızmaz.
 */
public sealed class AuthService(
    AppDbContext db,
    JwtTokenService jwt,
    IPasswordHasher<User> hasher,
    IOptions<JwtOptions> options,
    ILogger<AuthService> logger)
{
    // Kullanıcı bulunamadığında da bir hash doğrulaması yapılır; yanıt süresinden kullanıcı adının var olup olmadığı anlaşılmasın.
    private static readonly string DummyPasswordHash = new PasswordHasher<User>().HashPassword(new User(), "dummy-password");

    public async Task<TokenResponse?> LoginAsync(string userName, string password, CancellationToken ct)
    {
        var normalized = userName.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.UserName == normalized, ct);

        if (user?.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(new User(), DummyPasswordHash, password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed || !user.IsActive)
            return null;

        // Hash algoritması/iterasyon sayısı güncellendiyse şifre yeni ayarlarla tekrar hash'lenir.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);

        user.LastLoginAt = DateTime.Now;
        return await IssueTokensAsync(user, ct);
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.TokenHash == hash, ct);

        if (stored is null)
            return null;

        if (stored.RevokedAt is not null)
        {
            logger.LogWarning("İptal edilmiş refresh token tekrar kullanıldı; kullanıcının tüm oturumları kapatılıyor. UserId: {UserId}", stored.UserId);
            await RevokeAllAsync(stored.UserId, ct);
            return null;
        }

        if (stored.ExpiresAt <= DateTime.Now || !stored.User.IsActive)
            return null;

        stored.RevokedAt = DateTime.Now;
        return await IssueTokensAsync(stored.User, ct, replaced: stored);
    }

    // Çıkış: verilen refresh token iptal edilir. Token bulunamasa da hata verilmez (çağırana bilgi sızmasın).
    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAt is not null)
            return;

        stored.RevokedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    // Şifre değişimi, kullanıcı pasife alınması veya token çalınması şüphesinde kullanıcının tüm oturumlarını kapatır.
    // Kullanıcı başına aktif token sayısı az olduğu için yükleyip güncellemek yeterli (ExecuteUpdate testlerdeki bellek içi DB'de yok).
    public async Task RevokeAllAsync(int userId, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active)
            token.RevokedAt = DateTime.Now;

        await db.SaveChangesAsync(ct);
    }

    private async Task<TokenResponse> IssueTokensAsync(User user, CancellationToken ct, RefreshToken? replaced = null)
    {
        // Etkin roller: kullanıcıya doğrudan verilenler + üyesi olduğu grupların rolleri.
        var roles = await db.Roles
            .Where(r => r.UserRoles.Any(ur => ur.UserId == user.Id)
                        || r.GroupRoles.Any(gr => gr.Group.UserGroups.Any(ug => ug.UserId == user.Id)))
            .Select(r => r.Name)
            .ToListAsync(ct);

        var access = jwt.CreateAccessToken(user.Id, user.UserName, roles);

        var rawRefreshToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64));
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(rawRefreshToken),
            ExpiresAt = DateTime.Now.AddDays(options.Value.RefreshTokenExpiryDays)
        };
        db.RefreshTokens.Add(refresh);

        if (replaced is not null)
            replaced.ReplacedByTokenHash = refresh.TokenHash;

        // Kullanıcı güncellemesi (LastLoginAt), eski token'ın iptali ve yeni token tek SaveChanges'ta (tek transaction).
        await db.SaveChangesAsync(ct);

        return new TokenResponse(access.Token, "Bearer", access.ExpiresAt, rawRefreshToken, new DateTimeOffset(refresh.ExpiresAt));
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}