using System.Security.Claims;
using System.Text;
using MatchForecast.Api.Options;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MatchForecast.Api.Services;

/// <summary>HS256 imzalı JWT access token üretir.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    // JsonWebTokenHandler thread-safe; JwtBearer (.NET 8+) doğrulamada da aynı handler'ı kullanır.
    private static readonly JsonWebTokenHandler Handler = new();

    public TokenResponse CreateToken()
    {
        var o = options.Value;
        var expiresAt = DateTime.Now.AddMinutes(o.ExpiryMinutes);

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Expires = expiresAt,
            // Kimlik bilgisi alınmadığı için her token kendine özgü bir id taşır (ileride iptal/rate limit için anahtar olarak kullanılabilir).
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())]),
            SigningCredentials = new SigningCredentials(CreateSigningKey(o.SecretKey), SecurityAlgorithms.HmacSha256)
        });

        return new TokenResponse(token, "Bearer", new DateTimeOffset(expiresAt));
    }

    /// <summary>Token üretimi ve doğrulaması (Program.cs) aynı anahtarı kullansın diye tek yerde.</summary>
    public static SymmetricSecurityKey CreateSigningKey(string secretKey) => new(Encoding.UTF8.GetBytes(secretKey));
}