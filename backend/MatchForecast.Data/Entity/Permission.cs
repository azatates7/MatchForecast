namespace MatchForecast.Data.Entity;

/*
 * Yetki tanımı (ör. "users.manage"). Rollere atanır; kullanıcının yetkileri, etkin rollerinin (doğrudan + grup) yetkileridir.
 * - IsSystem=true: kodun kontrol ettiği yetki (PermissionKeys). Anahtarı değiştirilemez, silinemez; seed ile gelir.
 * - IsSystem=false: admin panelinden eklenen özel yetki. Kod (API policy veya frontend) anahtarını kontrol edene kadar etkisi yoktur.
 * Admin rolü tüm yetkilere örtük olarak sahiptir; ona ayrıca atama yapılmaz.
 */
public sealed class Permission
{
    public int Id { get; set; }

    // Küçük harf, "alan.işlem" biçiminde (ör. "users.view"); JWT'deki "perm" claim'inin değeri.
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;   // Türkçe görünen ad
    public string NameEn { get; set; } = string.Empty; // İngilizce görünen ad
    public string? Description { get; set; }
    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<RolePermission> RolePermissions { get; set; } = [];
}