namespace MatchForecast.Data.Entity;

public sealed class User
{
    public int Id { get; set; }

    // Küçük harfe çevrilerek saklanır (UsersController); unique index büyük/küçük harf farkını yakalamadığı için.
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    // Login adımında doldurulacak. Yalnızca harici sağlayıcıyla (Google/GitHub) giriş yapan kullanıcılarda boş kalır.
    public string? PasswordHash { get; set; }

    public bool EmailConfirmed { get; set; }

    // Varsayılan C# tarafında: bool için DB default'u verilseydi EF, false'u "değer girilmemiş" sayıp hep true yazardı.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public List<UserRole> UserRoles { get; set; } = [];
    public List<UserGroup> UserGroups { get; set; } = [];
    public List<UserLogin> Logins { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}