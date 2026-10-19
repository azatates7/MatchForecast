using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

// Kullanıcıya doğrudan verilen rol (users <-> roles n-n).
public sealed class UserRole : BaseEntity
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime AssignedAt { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}