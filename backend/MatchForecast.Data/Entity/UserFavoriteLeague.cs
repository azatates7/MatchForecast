namespace MatchForecast.Data.Entity;

// Kullanıcının favori ligi. League, frontend'deki lig anahtarıdır: "Ülke / Lig" (ör. "Turkey / Süper Lig").
public sealed class UserFavoriteLeague
{
    public int UserId { get; set; }
    public string League { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}