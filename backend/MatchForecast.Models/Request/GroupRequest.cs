using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Grup oluşturma/düzenleme. Ad büyük/küçük harf duyarsız benzersizdir.
public sealed record GroupRequest(
    [Required, MaxLength(100)] string Name,
    [MaxLength(250)] string? Description);

// Grubun rollerini verilen listeyle değiştirir; boş liste tüm rolleri kaldırır.
public sealed record GroupRolesRequest([Required] int[] RoleIds);

// Grubun üyelerini verilen listeyle değiştirir; boş liste tüm üyeleri çıkarır.
public sealed record GroupUsersRequest([Required] int[] UserIds);