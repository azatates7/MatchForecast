namespace MatchForecast.Models.Response;

// Giriş yapmış kullanıcının ekranda gösterilecek bilgileri (üst bardaki ad, misafir kısıtları için roller).
public sealed record MeResponse(string UserName, string? DisplayName, IReadOnlyList<string> Roles);