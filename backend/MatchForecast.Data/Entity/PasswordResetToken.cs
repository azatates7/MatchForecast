using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

// "Şifremi unuttum" bağlantısındaki tek kullanımlık token. Refresh token gibi yalnızca SHA-256 hash'i saklanır.
public sealed class PasswordResetToken : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    // Kullanıldığında (veya aynı kullanıcı şifresini başka bir bağlantıyla sıfırladığında) dolar; tekrar kullanılamaz.
    public DateTime? UsedAt { get; set; }
}