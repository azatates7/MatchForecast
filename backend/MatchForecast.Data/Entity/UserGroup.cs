using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

// Kullanıcının üye olduğu grup (users <-> groups n-n).
public sealed class UserGroup : BaseEntity
{
    public int UserId { get; set; }
    public int GroupId { get; set; }
    public DateTime AssignedAt { get; set; }

    public User User { get; set; } = null!;
    public Group Group { get; set; } = null!;
}