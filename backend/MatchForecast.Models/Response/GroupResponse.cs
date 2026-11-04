namespace MatchForecast.Models.Response;

// Admin panelindeki grup listesi. Atama uçları id istediği için roller ve üyeler id olarak döner.
public sealed record GroupResponse(
    int Id,
    string Name,
    string? Description,
    IReadOnlyList<int> RoleIds,
    IReadOnlyList<int> MemberIds);