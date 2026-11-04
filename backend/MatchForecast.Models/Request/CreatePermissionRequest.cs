using System.ComponentModel.DataAnnotations;
using MatchForecast.Models.Common;

namespace MatchForecast.Models.Request;

/*
 * Özel yetki tanımı (admin paneli). Anahtar küçük harfe çevrilerek saklanır ("Reports.View" -> "reports.view").
 * Yeni bir yetki, kod (API [RequirePermission] veya frontend) anahtarını kontrol edene kadar hiçbir şeyi açmaz.
 */
public sealed record CreatePermissionRequest(
    [Required, MaxLength(100), RegularExpression(PermissionKeyPattern.Value,
        ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.PermissionKeyFormat))]
    string Key,
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(100)] string NameEn,
    [MaxLength(500)] string? Description);

// Key null ise anahtar değişmez. Sistem yetkilerinde yalnızca ad ve açıklama değiştirilebilir.
public sealed record UpdatePermissionRequest(
    [MaxLength(100), RegularExpression(PermissionKeyPattern.Value,
        ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.PermissionKeyFormat))]
    string? Key,
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(100)] string NameEn,
    [MaxLength(500)] string? Description);

public static class PermissionKeyPattern
{
    // "area.action" (en az iki parça); parçalar harf, rakam ve '-' (ör. "app-settings.manage").
    public const string Value = @"^[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)+$";
}