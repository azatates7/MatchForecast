using System.Security.Claims;
using MatchForecast.Data.Seed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Security;

/*
 * Yetki (permission) tabanlı yetkilendirme.
 * - Endpoint'e [RequirePermission(PermissionKeys.UsersManage)] yazılır; birden fazla anahtar verilirse BİRİ yeterlidir.
 * - Policy adı "perm:{anahtar}" (veya "perm:a|b"); PermissionPolicyProvider bu adları çalışma anında policy'ye çevirir,
 *   yani yeni bir yetki için Program.cs'e kayıt eklemek gerekmez.
 * - Kontrol token'daki "perm" claim'lerine bakar; Admin rolü her yetkiye (sonradan eklenenler dahil) sahiptir.
 */
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(params string[] anyOf) => Policy = PermissionPolicy.Name(anyOf);
}

public static class PermissionPolicy
{
    public const string Prefix = "perm:";
    private const char Separator = '|';

    public static string Name(params string[] anyOf) => Prefix + string.Join(Separator, anyOf);

    // Controller içinde koşullu kontroller için (ör. refresh=true yalnızca forecast.refresh yetkisiyle).
    public static bool HasPermission(ClaimsPrincipal user, string key) =>
        user.IsInRole(RoleNames.Admin) || user.HasClaim(JwtTokenService.PermissionClaimType, key);

    internal static string[]? ParseKeys(string policyName) =>
        policyName.StartsWith(Prefix, StringComparison.Ordinal)
            ? policyName[Prefix.Length..].Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;
}

// "perm:" ile başlamayan adlar (fallback policy, NotGuest...) varsayılan sağlayıcıya bırakılır.
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (PermissionPolicy.ParseKeys(policyName) is not { Length: > 0 } keys)
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(c => keys.Any(key => PermissionPolicy.HasPermission(c.User, key)))
            .Build();
    }
}