namespace MatchForecast.Models.Response;

// Admin panelindeki grup seçimi için (PUT /api/users/{id}/groups id listesi ister).
public sealed record GroupResponse(int Id, string Name, string? Description);