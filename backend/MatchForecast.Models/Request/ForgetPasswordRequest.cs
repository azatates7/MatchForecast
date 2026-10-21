using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

public sealed record ForgotPasswordRequest([Required, EmailAddress, MaxLength(256)] string Email);