using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

public sealed class Role : BaseEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    public List<UserRole> UserRoles { get; set; } = [];
    public List<GroupRole> GroupRoles { get; set; } = [];
}