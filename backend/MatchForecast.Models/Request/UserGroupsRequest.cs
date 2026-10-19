using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Kullanıcının gruplarını verilen listeyle değiştirir; boş liste tüm gruplardan çıkarır.
public sealed record UserGroupsRequest([Required] int[] GroupIds);