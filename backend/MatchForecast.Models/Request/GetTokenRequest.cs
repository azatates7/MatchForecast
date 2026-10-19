using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Alan adı "password": LoggingMiddleware bu alanı request log'unda "***" ile maskeler.
public sealed record GetTokenRequest(
    [Required, MaxLength(50)] string Username,
    [Required, MaxLength(128)] string Password);