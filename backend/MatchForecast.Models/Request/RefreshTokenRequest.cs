using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Alan adı "refreshToken": LoggingMiddleware bu alanı request log'unda "***" ile maskeler.
public sealed record RefreshTokenRequest([Required, MaxLength(200)] string RefreshToken);