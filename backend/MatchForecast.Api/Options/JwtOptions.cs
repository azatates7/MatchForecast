using System.Security.Cryptography;
using System.Text;

namespace MatchForecast.Api.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    // HS256 için en az 32 byte (256 bit) olmalı; Program.cs'te ValidateOnStart ile kontrol edilir.
    public string SecretKey { get; set; } = "";
    public string Issuer { get; set; } = "MatchForecast.Api";
    public string Audience { get; set; } = "MatchForecast.Client";

    // Access token ömrü. Refresh token olduğu için kısa tutulabilir.
    public int ExpiryMinutes { get; set; } = 60;

    // Refresh token ömrü; bu süre boyunca kullanıcı tekrar şifre girmeden oturumunu yenileyebilir.
    public int RefreshTokenExpiryDays { get; set; } = 7;

    /*
     * Git geçmişinde (eski appsettings.json commit'leri) açık metin olarak duran SecretKey değerlerinin SHA-256 özetleri.
     * Ayarlar ilk açılışta app_settings tablosuna kopyalandığı için eski anahtar DB'de kalmış olabilir; bu anahtarı bilen biri
     * istediği rolle (Admin dahil) geçerli token üretebilir. Program.cs'te ValidateOnStart ile kontrol edilir: eşleşirse
     * uygulama açılmaz. Değerlerin kendisi değil yalnızca özetleri tutulur.
     */
    private static readonly HashSet<string> LeakedKeyHashes =
    [
        "e49f37f3e5a92c7e1be99bb75b7c181bef25e45e7e8fcff4ee8a160508f58fa2",
        "b7f1e21ee460b66255c5d77475ec9f22ecb1607d23c637ae70bf36670b9eb0c3",
    ];

    public static bool IsLeakedKey(string secretKey) =>
        LeakedKeyHashes.Contains(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secretKey))));
}