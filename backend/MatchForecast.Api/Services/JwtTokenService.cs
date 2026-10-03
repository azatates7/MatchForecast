using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MatchForecast.Api.Options;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MatchForecast.Api.Services;

/// <summary>Kimlik bilgisini doğrular ve HS256 imzalı JWT access token üretir.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    // JsonWebTokenHandler thread-safe; JwtBearer (.NET 8+) doğrulamada da aynı handler'ı kullanır.
    private static readonly JsonWebTokenHandler Handler = new();

    public bool ValidateCredentials(string? username, string? password)
    {
        var o = options.Value;
        if (string.IsNullOrEmpty(o.Username) || string.IsNullOrEmpty(o.Password))
        {
            return false; // Kimlik bilgisi tanımlanmamışsa hiçbir istek token alamaz.
        }

        // '&' (short-circuit değil): iki karşılaştırma da her zaman çalışır, süre farkından bilgi sızmaz.
        return FixedTimeEquals(username, o.Username) & FixedTimeEquals(password, o.Password);
    }

    public TokenResponse CreateToken(string username)
    {
        var o = options.Value;
        var expiresAt = DateTime.UtcNow.AddMinutes(o.ExpiryMinutes);

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            SigningCredentials = new SigningCredentials(CreateSigningKey(o.SecretKey), SecurityAlgorithms.HmacSha256)
        });

        return new TokenResponse(token, "Bearer", new DateTimeOffset(expiresAt));
    }

    /// <summary>Token üretimi ve doğrulaması (Program.cs) aynı anahtarı kullansın diye tek yerde.</summary>
    public static SymmetricSecurityKey CreateSigningKey(string secretKey) => new(Encoding.UTF8.GetBytes(secretKey));

    private static bool FixedTimeEquals(string? actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual ?? ""), Encoding.UTF8.GetBytes(expected));
}