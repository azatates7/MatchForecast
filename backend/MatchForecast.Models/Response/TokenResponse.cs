namespace MatchForecast.Models.Response;

// AccessToken kısa ömürlü (Jwt:ExpiryMinutes); süresi dolunca RefreshToken ile POST /api/auth/refresh'ten yenisi alınır.
// RefreshToken tek kullanımlıktır: her yenilemede yenisi döner, eskisi geçersiz olur.
public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);