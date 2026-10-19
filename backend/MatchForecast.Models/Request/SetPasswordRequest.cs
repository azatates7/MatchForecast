using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

public sealed record SetPasswordRequest([Required, MinLength(8), MaxLength(128)] string Password);