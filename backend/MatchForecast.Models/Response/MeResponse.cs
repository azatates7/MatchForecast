namespace MatchForecast.Models.Response;

// Giriş yapmış kullanıcının ekranda gösterilecek bilgileri (üst bardaki ad, misafir kısıtları için roller).
// Permissions: token'daki etkin yetki anahtarları; frontend özellikleri bunlara göre gösterir/gizler.
public sealed record MeResponse(string UserName, string? DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);