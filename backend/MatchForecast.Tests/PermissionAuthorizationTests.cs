using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MatchForecast.Tests;

/*
 * Yetki tabanlı yetkilendirme: [RequirePermission] token'daki "perm" claim'ine bakar, Admin rolü her yetkiye sahiptir;
 * token'a yazılan yetkiler kullanıcının doğrudan ve grup üzerinden gelen rollerinden hesaplanır.
 */
public class PermissionAuthorizationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] NoRoles = [];

    [Fact]
    public async Task CacheClear_RequiresPermission_NotAdminRole()
    {
        var testFactory = factory.WithTestJwt();

        var withPermission = testFactory.CreateClientFor(1, NoRoles, PermissionKeys.CacheClear);
        var without = testFactory.CreateClientFor(2, [RoleNames.User]);

        Assert.Equal(HttpStatusCode.Forbidden, (await without.DeleteAsync("/api/cache")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await withPermission.DeleteAsync("/api/cache")).StatusCode);
    }

    [Fact]
    public async Task Users_ViewAllowsReading_ManageAllowsChanges()
    {
        var testFactory = factory.WithTestJwt();
        var viewer = testFactory.CreateClientFor(1, NoRoles, PermissionKeys.UsersView);
        var manager = testFactory.CreateClientFor(2, NoRoles, PermissionKeys.UsersView, PermissionKeys.UsersManage);
        var newUser = new { userName = "yeni.kullanici", email = "yeni@test.local", displayName = "Yeni", password = "Test-Password-123" };

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/users", newUser)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/users", newUser)).StatusCode);
    }

    [Fact]
    public async Task RoleList_AcceptsAnyOfItsPermissions()
    {
        var testFactory = factory.WithTestJwt();

        Assert.Equal(HttpStatusCode.OK, (await testFactory.CreateClientFor(1, NoRoles, PermissionKeys.RolesManage).GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await testFactory.CreateClientFor(2, NoRoles, PermissionKeys.UsersView).GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await testFactory.CreateClientFor(3, NoRoles, PermissionKeys.CacheClear).GetAsync("/api/roles")).StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsPermissionsFromToken()
    {
        var testFactory = factory.WithTestJwt();
        var client = testFactory.CreateClientFor(1, [RoleNames.User], PermissionKeys.CacheClear, "reports.view");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.Equal(new[] { PermissionKeys.CacheClear, "reports.view" }, me!.Permissions.ToArray());
    }

    [Fact]
    public void HasPermission_AdminHasEverything_OthersNeedTheClaim()
    {
        static ClaimsPrincipal Principal(params Claim[] claims) =>
            new(new ClaimsIdentity(claims, "test", JwtTokenService.NameClaimType, JwtTokenService.RoleClaimType));

        var admin = Principal(new Claim(JwtTokenService.RoleClaimType, RoleNames.Admin));
        var user = Principal(new Claim(JwtTokenService.PermissionClaimType, PermissionKeys.UsersView));

        Assert.True(PermissionPolicy.HasPermission(admin, "anything.added.later"));
        Assert.True(PermissionPolicy.HasPermission(user, PermissionKeys.UsersView));
        Assert.False(PermissionPolicy.HasPermission(user, PermissionKeys.UsersManage));
    }

    [Fact]
    public async Task Token_ContainsEffectivePermissions_FromDirectAndGroupRoles()
    {
        var testFactory = factory.WithTestJwt();
        using (var scope = testFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

            var usersView = new Permission { Key = PermissionKeys.UsersView, Name = "a", NameEn = "a", IsSystem = true };
            var cacheClear = new Permission { Key = PermissionKeys.CacheClear, Name = "b", NameEn = "b", IsSystem = true };
            var reports = new Permission { Key = "reports.view", Name = "c", NameEn = "c" }; // Panelden eklenmiş özel yetki
            var unused = new Permission { Key = PermissionKeys.RolesManage, Name = "d", NameEn = "d", IsSystem = true };
            db.Permissions.AddRange(usersView, cacheClear, reports, unused);

            var editor = new Role { Name = "Editor" };
            editor.RolePermissions.Add(new RolePermission { Permission = usersView });
            var moderator = new Role { Name = "Moderator" };
            moderator.RolePermissions.Add(new RolePermission { Permission = cacheClear });
            moderator.RolePermissions.Add(new RolePermission { Permission = reports });
            var adminRole = new Role { Name = RoleNames.Admin, IsSystem = true };

            var team = new Group { Name = "Ekip" };
            team.GroupRoles.Add(new GroupRole { Role = moderator });

            var member = new User { UserName = "uye", Email = "uye@test.local" };
            member.PasswordHash = hasher.HashPassword(member, TestAuth.Password);
            member.UserRoles.Add(new UserRole { Role = editor });
            member.UserGroups.Add(new UserGroup { Group = team });

            var admin = new User { UserName = "yonetici", Email = "yonetici@test.local" };
            admin.PasswordHash = hasher.HashPassword(admin, TestAuth.Password);
            admin.UserRoles.Add(new UserRole { Role = adminRole });

            db.Users.AddRange(member, admin);
            await db.SaveChangesAsync();
        }

        var client = testFactory.CreateClient();

        Assert.Equal(
            new[] { PermissionKeys.CacheClear, "reports.view", PermissionKeys.UsersView },
            await PermissionsInTokenAsync(client, "uye"));
        // Admin: tablodaki tüm yetkiler, kendisine atanmamış olanlar dahil.
        Assert.Equal(
            new[] { PermissionKeys.CacheClear, "reports.view", PermissionKeys.RolesManage, PermissionKeys.UsersView },
            await PermissionsInTokenAsync(client, "yonetici"));
    }

    private static async Task<string[]> PermissionsInTokenAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new { username = userName, password = TestAuth.Password });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return new JsonWebToken(token!.AccessToken).Claims
            .Where(c => c.Type == JwtTokenService.PermissionClaimType)
            .Select(c => c.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}