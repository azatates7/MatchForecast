namespace MatchForecast.Data.Entity;

// Supabase bağlantısını denemek için örnek tablo; gerçek tablolar eklendiğinde silinebilir.
public sealed class SampleItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Değeri veritabanı verir (now()); Supabase Table Editor'dan eklenen satırlar da dolu gelir.
    public DateTime CreatedAt { get; set; }
}