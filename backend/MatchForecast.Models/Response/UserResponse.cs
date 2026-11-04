namespace MatchForecast.Models.Response;

// Entity yerine DTO döner: PasswordHash gibi alanlar hiçbir zaman API yanıtına çıkmaz.
// Roles/Groups: doğrudan atamalar (ad). Permissions: etkin yetkiler (doğrudan + grup rolleri; Admin → tümü), salt okunur.
public sealed record UserResponse(
    int Id,
    string UserName,
    string Email,
    string? DisplayName,
    bool IsActive,
    bool EmailConfirmed,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastLoginAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Permissions);