using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

// Gruba verilen rol (groups <-> roles çoka-çok).
public sealed class GroupRole : BaseEntity
{
    public int GroupId { get; set; }
    public int RoleId { get; set; }
    public DateTime AssignedAt { get; set; }

    public Group Group { get; set; } = null!;
    public Role Role { get; set; } = null!;
}