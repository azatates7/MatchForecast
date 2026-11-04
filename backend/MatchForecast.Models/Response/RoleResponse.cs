namespace MatchForecast.Models.Response;

/*
 * Admin panelindeki rol listesi. Atama uçları id istediği için yetkiler id olarak döner (PermissionIds).
 * - HasAllPermissions: Admin rolü; tüm yetkilere örtük olarak sahiptir, PermissionIds boştur ve düzenlenemez.
 * - UserCount / GroupCount: rolün doğrudan atandığı kullanıcı ve grup sayısı (silme onayı için).
 */
public sealed record RoleResponse(
    int Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool HasAllPermissions,
    IReadOnlyList<int> PermissionIds,
    int UserCount,
    int GroupCount);