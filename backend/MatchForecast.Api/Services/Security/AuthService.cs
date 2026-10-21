using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Email;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Request;
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
 * - Kayıt, Google ile giriş ve şifre sıfırlama da burada; hepsi sonunda aynı token çiftini üretir.
 */
public sealed partial class AuthService(
    AppDbContext db,
    JwtTokenService jwt,
    IPasswordHasher<User> hasher,
    IOptions<JwtOptions> options,
    IOptions<AuthOptions> authOptions,
    IEmailSender emailSender,
    ILogger<AuthService> logger)
{
    public const string GoogleProvider = "Google";

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

    // Yeni kullanıcı "User" rolüyle açılır ve doğrudan giriş yapmış olur. Hata mesajı döner veya token.
    public async Task<(TokenResponse? Token, string? Error)> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(x => x.UserName == userName, ct))
            return (null, "Bu kullanıcı adı kullanılıyor.");

        if (await db.Users.AnyAsync(x => x.Email == email, ct))
            return (null, "Bu e-posta adresiyle zaten bir hesap var. Giriş yapın veya şifrenizi sıfırlayın.");

        var user = new User { UserName = userName, Email = email, DisplayName = request.DisplayName };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        await AddDefaultRoleAsync(user, ct);
        user.LastLoginAt = DateTime.Now;
        db.Users.Add(user);
        await db.SaveChangesAsync(ct); // Token üretmeden önce kullanıcının Id'si oluşmalı.

        return (await IssueTokensAsync(user, ct), null);
    }

    /*
     * Google hesabı daha önce bağlandıysa o kullanıcıyla girer. Bağlı değilse, Google'ın DOĞRULADIĞI e-posta ile eşleşen
     * mevcut hesaba bağlanır; o da yoksa yeni kullanıcı açılır. Doğrulanmamış e-postayla eşleştirme yapılmaz: aksi halde
     * başkasının e-postasını yazan biri o kişinin hesabına girebilirdi.
     */
    public async Task<TokenResponse?> GoogleLoginAsync(GoogleUser google, CancellationToken ct)
    {
        var login = await db.UserLogins.Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Provider == GoogleProvider && x.ProviderKey == google.Subject, ct);

        User user;
        if (login is not null)
        {
            user = login.User;
            login.UpdatedAt = DateTime.Now;
        }
        else
        {
            if (!google.EmailVerified)
                return null;

            var email = google.Email.Trim().ToLowerInvariant();
            user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, ct) ?? await CreateGoogleUserAsync(google, email, ct);
            user.EmailConfirmed = true;
            user.Logins.Add(new UserLogin
            {
                Provider = GoogleProvider,
                ProviderKey = google.Subject,
                ProviderDisplayName = google.Name,
                UpdatedAt = DateTime.Now
            });
        }

        if (!user.IsActive)
            return null;

        user.LastLoginAt = DateTime.Now;
        await db.SaveChangesAsync(ct); // Yeni kullanıcıysa Id oluşsun.
        return await IssueTokensAsync(user, ct);
    }

    /*
     * Her durumda aynı şekilde döner (e-posta kayıtlı olsun olmasın): yanıttan hangi e-postaların kayıtlı olduğu anlaşılmasın.
     * E-posta arka planda gönderilir; SMTP süresi yanıt süresine yansıyıp aynı bilgiyi sızdırmasın.
     */
    public async Task RequestPasswordResetAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == normalized && x.IsActive, ct);
        if (user is null)
            return;

        var o = authOptions.Value;
        var rawToken = NewToken();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = Hash(rawToken),
            ExpiresAt = DateTime.Now.AddMinutes(o.PasswordResetTokenMinutes)
        });
        await db.SaveChangesAsync(ct);

        var link = $"{o.PasswordResetUrl.TrimEnd('/')}/?resetToken={Uri.EscapeDataString(rawToken)}";
        var body = $"""
            <p>Merhaba {WebUtility.HtmlEncode(user.DisplayName ?? user.UserName)},</p>
            <p>MatchForecast şifreni sıfırlamak için aşağıdaki bağlantıya tıkla. Bağlantı {o.PasswordResetTokenMinutes} dakika geçerli ve tek kullanımlıktır.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Şifremi sıfırla</a></p>
            <p>Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin; şifren değişmez.</p>
            """;

        // IEmailSender singleton; istek bittikten sonra DbContext'e dokunmadan çalışır.
        _ = Task.Run(async () =>
        {
            try
            {
                await emailSender.SendAsync([user.Email], "MatchForecast şifre sıfırlama", body, isHtml: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Şifre sıfırlama e-postası gönderilemedi. UserId: {UserId}", user.Id);
            }
        }, CancellationToken.None);
    }

    // Başarılıysa yeni şifre kaydedilir, bekleyen tüm sıfırlama bağlantıları ve açık oturumlar geçersiz olur.
    public async Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken ct)
    {
        var hash = Hash(token);
        var reset = await db.PasswordResetTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (reset is null || reset.UsedAt is not null || reset.ExpiresAt <= DateTime.Now || !reset.User.IsActive)
            return false;

        reset.User.PasswordHash = hasher.HashPassword(reset.User, newPassword);
        reset.User.UpdatedAt = DateTime.Now;

        var pending = await db.PasswordResetTokens.Where(x => x.UserId == reset.UserId && x.UsedAt == null).ToListAsync(ct);
        foreach (var item in pending)
            item.UsedAt = DateTime.Now;

        await db.SaveChangesAsync(ct);
        await RevokeAllAsync(reset.UserId, ct);
        return true;
    }

    // Kullanıcı adı e-postanın @ öncesinden türetilir; doluysa sonuna rastgele sayı eklenir.
    private async Task<User> CreateGoogleUserAsync(GoogleUser google, string email, CancellationToken ct)
    {
        var baseName = InvalidUserNameChars().Replace(email.Split('@')[0], "");
        if (baseName.Length < 3)
            baseName = "user";
        baseName = baseName[..Math.Min(baseName.Length, 40)];

        var userName = baseName;
        while (await db.Users.AnyAsync(x => x.UserName == userName, ct))
            userName = $"{baseName}{RandomNumberGenerator.GetInt32(1000, 10000)}";

        var user = new User { UserName = userName, Email = email, DisplayName = google.Name };
        await AddDefaultRoleAsync(user, ct);
        db.Users.Add(user);
        return user;
    }

    private async Task AddDefaultRoleAsync(User user, CancellationToken ct)
    {
        var roleId = await db.Roles.Where(x => x.Name == RoleNames.User).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (roleId != 0)
            user.UserRoles.Add(new UserRole { RoleId = roleId });
    }

    [GeneratedRegex("[^a-z0-9._-]")]
    private static partial Regex InvalidUserNameChars();

    private async Task<TokenResponse> IssueTokensAsync(User user, CancellationToken ct, RefreshToken? replaced = null)
    {
        // Etkin roller: kullanıcıya doğrudan verilenler + üyesi olduğu grupların rolleri.
        var roles = await db.Roles
            .Where(r => r.UserRoles.Any(ur => ur.UserId == user.Id)
                        || r.GroupRoles.Any(gr => gr.Group.UserGroups.Any(ug => ug.UserId == user.Id)))
            .Select(r => r.Name)
            .ToListAsync(ct);

        var access = jwt.CreateAccessToken(user.Id, user.UserName, roles);

        var rawRefreshToken = NewToken();
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

    // 64 byte kriptografik rastgele değer, URL'de güvenle taşınabilir (base64url).
    private static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64));

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}