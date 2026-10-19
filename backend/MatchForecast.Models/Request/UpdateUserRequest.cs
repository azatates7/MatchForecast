using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Kullanıcı adı değiştirilemez (kalıcı kimlik); e-posta değişirse doğrulama sıfırlanır.
public sealed record UpdateUserRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [MaxLength(100)] string? DisplayName,
    bool IsActive);