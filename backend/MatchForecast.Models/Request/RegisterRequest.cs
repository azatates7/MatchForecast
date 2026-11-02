using System.ComponentModel.DataAnnotations;
using MatchForecast.Models.Common;

namespace MatchForecast.Models.Request;

public sealed record RegisterRequest(
    [Required, RegularExpression("^[a-zA-Z0-9._-]{3,50}$",
        ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.UserNameFormat))]
    string UserName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [MaxLength(100)] string? DisplayName);