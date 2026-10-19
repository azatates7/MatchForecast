using System.Globalization;
using System.Security.Claims;
using System.Text;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MatchForecast.Api.Services.Security;

/// <summary>HS256 imzalı JWT access token üretir.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    // Kısa claim adları kullanılır; Program.cs'te MapInboundClaims=false ve Name/RoleClaimType bu adlara ayarlı.
    public const string NameClaimType = JwtRegisteredClaimNames.UniqueName; // "unique_name"
    public const string RoleClaimType = "role";

    // JsonWebTokenHandler thread-safe; JwtBearer (.NET 8+) doğrulamada da aynı handler'ı kullanır.
    private static readonly JsonWebTokenHandler Handler = new();

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(int userId, string userName, IEnumerable<string> roles)
    {
        var o = options.Value;
        var expiresAt = DateTime.Now.AddMinutes(o.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
            new(NameClaimType, userName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(RoleClaimType, role)));

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(CreateSigningKey(o.SecretKey), SecurityAlgorithms.HmacSha256)
        });

        return (token, new DateTimeOffset(expiresAt));
    }

    /// <summary>Token üretimi ve doğrulaması (Program.cs) aynı anahtarı kullansın diye tek yerde.</summary>
    public static SymmetricSecurityKey CreateSigningKey(string secretKey) => new(Encoding.UTF8.GetBytes(secretKey));
}