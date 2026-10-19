namespace MatchForecast.Models.Response;

// Entity yerine DTO döner: PasswordHash gibi alanlar hiçbir zaman API yanıtına çıkmaz.
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
    IReadOnlyList<string> Groups);