using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

// Kullanıcıya bağlı harici giriş hesabı (Google, GitHub...). Bir kullanıcının birden fazla sağlayıcısı olabilir.
public sealed class UserLogin : BaseEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public string Provider { get; set; } = string.Empty;      // "Google", "GitHub"
    public string ProviderKey { get; set; } = string.Empty;   // Sağlayıcıdaki kullanıcı kimliği (OIDC "sub")
    public string? ProviderDisplayName { get; set; }

    public User User { get; set; } = null!;
}