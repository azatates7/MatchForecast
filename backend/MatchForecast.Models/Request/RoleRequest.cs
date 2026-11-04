using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Rol oluşturma/düzenleme. Ad büyük/küçük harf duyarsız benzersizdir; sistem rollerinin adı değiştirilemez.
public sealed record RoleRequest(
    [Required, MaxLength(50)] string Name,
    [MaxLength(250)] string? Description);

// Rolün yetkilerini verilen listeyle değiştirir; boş liste tüm yetkileri kaldırır. Admin rolü için kullanılamaz.
public sealed record RolePermissionsRequest([Required] int[] PermissionIds);