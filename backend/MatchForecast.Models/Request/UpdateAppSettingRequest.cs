using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

/*
 * Value null ise mevcut değer korunur; böylece gizli bir değeri yeniden yazmadan yalnızca açıklama veya şifreleme
 * durumu değiştirilebilir. "value" alanı LoggingMiddleware'de maskelenir.
 */
public sealed record UpdateAppSettingRequest(
    [MaxLength(4000)] string? Value,
    bool IsEncrypted,
    [MaxLength(500)] string? Description);