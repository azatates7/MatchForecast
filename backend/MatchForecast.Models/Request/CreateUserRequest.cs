using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

public sealed record CreateUserRequest(
    [Required, RegularExpression("^[a-zA-Z0-9._-]{3,50}$",
        ErrorMessage = "Kullanıcı adı 3-50 karakter olmalı; yalnızca harf, rakam, '.', '_' ve '-' içerebilir.")]
    string UserName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [MaxLength(100)] string? DisplayName,
    [Required, MinLength(8), MaxLength(128)] string Password);