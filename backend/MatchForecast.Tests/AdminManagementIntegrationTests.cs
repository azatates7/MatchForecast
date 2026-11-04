using System.Net;
using System.Net.Http.Json;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

/*
 * Admin paneli yönetim uçları (adım 8.2): yetki tanımları, roller, gruplar ve kullanıcıların etkin yetkileri.
 * Sistem rolleri/yetkileri gerçek seed (RoleSeed, PermissionSeed) ile eklenir; bellek içi DB'de migration çalışmaz.
 */
public class AdminManagementIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const int OtherAdminId = 999; // Token'daki kullanıcı DB'de yok; Admin rolü claim'den gelir.
    private static readonly string[] AdminRole = [RoleNames.Admin];

    private sealed record Seeded(
        WebApplicationFactory<Program> Factory,
        Dictionary<string, int> RoleIds,
        Dictionary<string, int> PermissionIds,
        int? AdminUserId);

    private async Task<Seeded> SeedSystemAsync(bool withAdminUser = true)
    {
        var testFactory = factory.WithTestJwt();
        using var scope = testFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RoleSeed.SeedAsync(db, false, CancellationToken.None);
        await PermissionSeed.SeedAsync(db, false, CancellationToken.None);

        int? adminUserId = null;
        if (withAdminUser)
        {
            var admin = new User { UserName = "admin", Email = "admin@test.local" };
            admin.UserRoles.Add(new UserRole { RoleId = await db.Roles.Where(x => x.Name == RoleNames.Admin).Select(x => x.Id).FirstAsync() });
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            adminUserId = admin.Id;
        }

        return new Seeded(
            testFactory,
            await db.Roles.ToDictionaryAsync(x => x.Name, x => x.Id),
            await db.Permissions.ToDictionaryAsync(x => x.Key, x => x.Id),
            adminUserId);
    }

    private static async Task<T> InDbAsync<T>(Seeded seeded, Func<AppDbContext, Task<T>> action)
    {
        using var scope = seeded.Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<int> AddUserAsync(Seeded seeded, string userName, params int[] roleIds) =>
        await InDbAsync(seeded, async db =>
        {
            var user = new User { UserName = userName, Email = $"{userName}@test.local" };
            foreach (var roleId in roleIds)
                user.UserRoles.Add(new UserRole { RoleId = roleId });
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });

    private static async Task<T> CreatedAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    // ---------- Yetki tanımları ----------

    [Fact]
    public async Task CustomPermission_CanBeCreatedRenamedAndDeleted()
    {
        var seeded = await SeedSystemAsync();
        var admin = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);

        var created = await CreatedAsync<PermissionResponse>(await admin.PostAsJsonAsync("/api/permissions",
            new { key = "Reports.View", name = "Raporları görme", nameEn = "View reports", description = (string?)null }));
        Assert.Equal("reports.view", created.Key);
        Assert.False(created.IsSystem);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/permissions",
            new { key = "REPORTS.VIEW", name = "x", nameEn = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/permissions",
            new { key = "reports", name = "x", nameEn = "x" })).StatusCode); // En az iki parça gerekir.

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/permissions/{created.Id}",
            new { key = "reports.read", name = "Raporları okuma", nameEn = "Read reports" })).StatusCode);
        Assert.Equal("reports.read", (await admin.GetFromJsonAsync<PermissionResponse>($"/api/permissions/{created.Id}"))!.Key);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/permissions/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/permissions/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task SystemPermission_KeepsItsKey_AndCannotBeDeleted()
    {
        var seeded = await SeedSystemAsync();
        var admin = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);
        var id = seeded.PermissionIds[PermissionKeys.CacheClear];

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/permissions/{id}",
            new { key = "cache.wipe", name = "x", nameEn = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/permissions/{id}",
            new { key = (string?)null, name = "Önbelleği temizleme", nameEn = "Clear the cache" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/permissions/{id}")).StatusCode);

        var permission = await admin.GetFromJsonAsync<PermissionResponse>($"/api/permissions/{id}");
        Assert.Equal(PermissionKeys.CacheClear, permission!.Key);
        Assert.Equal("Önbelleği temizleme", permission.Name);
    }

    [Fact]
    public async Task PermissionList_IsReadableWithRolesManage_ButChangesNeedPermissionsManage()
    {
        var seeded = await SeedSystemAsync();
        var roleManager = seeded.Factory.CreateClientFor(1, [], PermissionKeys.RolesManage);

        Assert.Equal(HttpStatusCode.OK, (await roleManager.GetAsync("/api/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await roleManager.PostAsJsonAsync("/api/permissions",
            new { key = "reports.view", name = "x", nameEn = "x" })).StatusCode);
    }

    // ---------- Roller ----------

    [Fact]
    public async Task Role_CanBeCreatedRenamedAndGivenPermissions()
    {
        var seeded = await SeedSystemAsync();
        var admin = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);

        var role = await CreatedAsync<RoleResponse>(await admin.PostAsJsonAsync("/api/roles", new { name = "Editor", description = "İçerik" }));
        Assert.False(role.IsSystem);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/roles", new { name = "editor" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/roles/{role.Id}", new { name = "Editör", description = "İçerik" })).StatusCode);

        var usersView = seeded.PermissionIds[PermissionKeys.UsersView];
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions", new { permissionIds = new[] { usersView } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions", new { permissionIds = new[] { 12345 } })).StatusCode);

        var updated = await admin.GetFromJsonAsync<RoleResponse>($"/api/roles/{role.Id}");
        Assert.Equal("Editör", updated!.Name);
        Assert.Equal(usersView, Assert.Single(updated.PermissionIds));
    }

    [Fact]
    public async Task SystemRoles_CannotBeRenamedOrDeleted_AndAdminPermissionsAreFixed()
    {
        var seeded = await SeedSystemAsync();
        var admin = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);
        var adminRoleId = seeded.RoleIds[RoleNames.Admin];
        var userRoleId = seeded.RoleIds[RoleNames.User];

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/roles/{adminRoleId}", new { name = "Yönetici" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/roles/{adminRoleId}", new { name = RoleNames.Admin, description = "Yeni açıklama" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/roles/{userRoleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/roles/{adminRoleId}/permissions", new { permissionIds = Array.Empty<int>() })).StatusCode);

        // Diğer sistem rollerine yetki verilebilir (ör. User rolüne cache.clear).
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/roles/{userRoleId}/permissions",
            new { permissionIds = new[] { seeded.PermissionIds[PermissionKeys.CacheClear] } })).StatusCode);

        var roles = await admin.GetFromJsonAsync<List<RoleResponse>>("/api/roles");
        var adminRole = roles!.Single(x => x.Id == adminRoleId);
        Assert.True(adminRole.HasAllPermissions);
        Assert.True(adminRole.IsSystem);
        Assert.Equal("Yeni açıklama", adminRole.Description);
    }

    [Fact]
    public async Task AssignedRole_IsDeletedOnlyWithForce()
    {
        var seeded = await SeedSystemAsync();
        var admin = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);
        var role = await CreatedAsync<RoleResponse>(await admin.PostAsJsonAsync("/api/roles", new { name = "Moderator" }));
        var userId = await AddUserAsync(seeded, "moderator1", role.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/roles/{role.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/roles/{role.Id}?force=true")).StatusCode);

        var user = await admin.GetFromJsonAsync<UserResponse>($"/api/users/{userId}");
        Assert.Empty(user!.Roles);
    }

    // ---------- Gruplar ----------

    [Fact]
    public async Task Group_CanBeCreatedGivenRolesAndMembers_AndDeleted()
    {
        var seeded = await SeedSystemAsync();
        var admin = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);
        var userId = await AddUserAsync(seeded, "uye1");

        var group = await CreatedAsync<GroupResponse>(await admin.PostAsJsonAsync("/api/groups", new { name = "Ekip" }));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/groups", new { name = "ekip" })).StatusCode);

        var userRoleId = seeded.RoleIds[RoleNames.User];
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/groups/{group.Id}/roles", new { roleIds = new[] { userRoleId } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/groups/{group.Id}/users", new { userIds = new[] { userId } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/groups/{group.Id}/users", new { userIds = new[] { 12345 } })).StatusCode);

        var loaded = await admin.GetFromJsonAsync<GroupResponse>($"/api/groups/{group.Id}");
        Assert.Equal(userRoleId, Assert.Single(loaded!.RoleIds));
        Assert.Equal(userId, Assert.Single(loaded.MemberIds));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<UserResponse>($"/api/users/{userId}"))!.Groups);
    }

    [Fact]
    public async Task AdminGroup_ProtectsTheLastAdmin_AndTheCallersOwnAccess()
    {
        var seeded = await SeedSystemAsync(withAdminUser: false);
        var adminRoleId = seeded.RoleIds[RoleNames.Admin];
        var other = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);

        // Tek Admin, Admin'liğini yalnızca gruptan alıyor.
        var groupAdminId = await AddUserAsync(seeded, "grupadmin");
        var group = await CreatedAsync<GroupResponse>(await other.PostAsJsonAsync("/api/groups", new { name = "Yöneticiler" }));
        Assert.Equal(HttpStatusCode.NoContent, (await other.PutAsJsonAsync($"/api/groups/{group.Id}/roles", new { roleIds = new[] { adminRoleId } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PutAsJsonAsync($"/api/groups/{group.Id}/users", new { userIds = new[] { groupAdminId } })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await other.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PutAsJsonAsync($"/api/groups/{group.Id}/roles", new { roleIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PutAsJsonAsync($"/api/groups/{group.Id}/users", new { userIds = Array.Empty<int>() })).StatusCode);

        // Başka bir aktif Admin varken: kişi kendini yine çıkaramaz, başkası çıkarabilir.
        await AddUserAsync(seeded, "ikinciadmin", adminRoleId);
        var self = seeded.Factory.CreateClientFor(groupAdminId, AdminRole);
        Assert.Equal(HttpStatusCode.BadRequest, (await self.PutAsJsonAsync($"/api/groups/{group.Id}/users", new { userIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PutAsJsonAsync($"/api/groups/{group.Id}/users", new { userIds = Array.Empty<int>() })).StatusCode);
    }

    [Fact]
    public async Task GroupChanges_NeedGroupsManage()
    {
        var seeded = await SeedSystemAsync();
        var viewer = seeded.Factory.CreateClientFor(1, [], PermissionKeys.UsersView);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/groups", new { name = "Ekip" })).StatusCode);
    }

    // ---------- Kullanıcıların etkin yetkileri ----------

    [Fact]
    public async Task Users_IncludeEffectivePermissions_FromDirectAndGroupRoles()
    {
        var seeded = await SeedSystemAsync();
        var admin = seeded.Factory.CreateClientFor(OtherAdminId, AdminRole);

        var editor = await CreatedAsync<RoleResponse>(await admin.PostAsJsonAsync("/api/roles", new { name = "Editor" }));
        var moderator = await CreatedAsync<RoleResponse>(await admin.PostAsJsonAsync("/api/roles", new { name = "Moderator" }));
        await admin.PutAsJsonAsync($"/api/roles/{editor.Id}/permissions", new { permissionIds = new[] { seeded.PermissionIds[PermissionKeys.UsersView] } });
        await admin.PutAsJsonAsync($"/api/roles/{moderator.Id}/permissions", new { permissionIds = new[] { seeded.PermissionIds[PermissionKeys.CacheClear] } });

        var userId = await AddUserAsync(seeded, "karma", editor.Id);
        var group = await CreatedAsync<GroupResponse>(await admin.PostAsJsonAsync("/api/groups", new { name = "Moderatörler" }));
        await admin.PutAsJsonAsync($"/api/groups/{group.Id}/roles", new { roleIds = new[] { moderator.Id } });
        await admin.PutAsJsonAsync($"/api/groups/{group.Id}/users", new { userIds = new[] { userId } });

        var user = await admin.GetFromJsonAsync<UserResponse>($"/api/users/{userId}");
        Assert.Equal(new[] { PermissionKeys.CacheClear, PermissionKeys.UsersView }, user!.Permissions.ToArray());

        var users = await admin.GetFromJsonAsync<List<UserResponse>>("/api/users");
        var adminUser = users!.Single(x => x.Id == seeded.AdminUserId);
        Assert.Equal(seeded.PermissionIds.Keys.Order(StringComparer.Ordinal).ToArray(), adminUser.Permissions.ToArray());
    }
}