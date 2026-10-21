using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// "token" ve "password" alanları log'da maskelenir.
public sealed record ResetPasswordRequest(
    [Required, MaxLength(200)] string Token,
    [Required, MinLength(8), MaxLength(128)] string Password);