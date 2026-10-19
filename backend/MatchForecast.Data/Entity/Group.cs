using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

// Kullanıcıları toplu yetkilendirmek için: gruba verilen roller, gruptaki her kullanıcıya geçer (login adımında hesaplanacak).
public sealed class Group : BaseEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    public List<UserGroup> UserGroups { get; set; } = [];
    public List<GroupRole> GroupRoles { get; set; } = [];
}