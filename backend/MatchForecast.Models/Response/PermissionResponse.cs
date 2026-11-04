namespace MatchForecast.Models.Response;

// RoleIds: bu yetkinin verildiği roller (Admin'e ayrıca atama yapılmaz; Admin her yetkiye örtük olarak sahiptir).
public sealed record PermissionResponse(
    int Id,
    string Key,
    string Name,
    string NameEn,
    string? Description,
    bool IsSystem,
    IReadOnlyList<int> RoleIds);