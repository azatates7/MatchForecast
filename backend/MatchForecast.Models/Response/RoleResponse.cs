namespace MatchForecast.Models.Response;

// Admin panelindeki rol seçimi için (PUT /api/users/{id}/roles id listesi ister).
public sealed record RoleResponse(int Id, string Name, string? Description);