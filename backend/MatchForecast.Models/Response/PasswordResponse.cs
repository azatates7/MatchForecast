namespace MatchForecast.Models.Response;

// Alan adı "password": LoggingMiddleware bu alanı response log'unda "***" ile maskeler.
public sealed record PasswordResponse(string Password, int Length);