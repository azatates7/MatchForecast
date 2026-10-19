using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Kullanıcının rollerini verilen listeyle değiştirir; boş liste tüm rolleri kaldırır.
public sealed record UserRolesRequest([Required] int[] RoleIds);