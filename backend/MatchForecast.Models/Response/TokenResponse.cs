namespace MatchForecast.Models.Response;

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);