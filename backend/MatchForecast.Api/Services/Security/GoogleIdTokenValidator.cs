using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OidcConfigurationManager = Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>;

namespace MatchForecast.Api.Services.Security;

public sealed record GoogleUser(string Subject, string Email, bool EmailVerified, string? Name);

/*
 * Google ID token'ını doğrular: imza (Google'ın yayınladığı anahtarlar), issuer, audience (bizim Client ID'miz) ve süre.
 * Ek paket gerekmez; JwtBearer'ın zaten kullandığı Microsoft.IdentityModel kütüphaneleri yeterli.
 */
public class GoogleIdTokenValidator(IOptions<AuthOptions> options)
{
    // Anahtarlar discovery dokümanından okunur ve cache'lenir; Google anahtarları değiştirdiğinde kendiliğinden yenilenir.
    private static readonly OidcConfigurationManager Configuration = new(
        "https://accounts.google.com/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever());

    private static readonly JsonWebTokenHandler Handler = new();

    // virtual: testlerde Google'a gitmeden sahte kullanıcı döndürmek için.
    public virtual async Task<GoogleUser?> ValidateAsync(string idToken, CancellationToken ct)
    {
        var clientId = options.Value.GoogleClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var config = await Configuration.GetConfigurationAsync(ct);
        var result = await Handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
            ValidAudience = clientId,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true
        });

        if (!result.IsValid)
            return null;

        string? Claim(string type) => result.Claims.TryGetValue(type, out var value) ? value?.ToString() : null;

        var subject = Claim("sub");
        var email = Claim("email");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
            return null;

        var emailVerified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new GoogleUser(subject, email, emailVerified, Claim("name"));
    }
}