using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Data.Seed;

/*
 * Geçici örnek veri. Migrate() her çağrıldığında migration'lardan sonra çalışır; tabloda kayıt varsa hiçbir şey yapmaz.
 * Kaldırmak için bu dosyayı ve AppDbContext.OnConfiguring'deki UseSeeding/UseAsyncSeeding satırlarını silmek yeterli.
 */
public static class SampleItemSeed
{
    // Her çağrıda yeni instance: aynı entity nesneleri iki kez track edilmesin.
    private static SampleItem[] CreateItems() =>
    [
        new() { Name = "Galatasaray - Fenerbahçe", Description = "Süper Lig derbi örneği" },
        new() { Name = "Real Madrid - Barcelona", Description = "La Liga El Clásico örneği" },
        new() { Name = "Liverpool - Manchester City", Description = "Premier League örneği" },
        new() { Name = "Inter - Milan", Description = "Serie A Derby della Madonnina örneği" },
        new() { Name = "Bayern München - Borussia Dortmund", Description = "Bundesliga Der Klassiker örneği" }
    ];

    // Senkron yol: dotnet ef database update bunu çağırır.
    public static void Seed(DbContext context, bool storeManagementPerformed)
    {
        var db = (AppDbContext)context;
        if (db.SampleItems.Any())
            return;

        db.SampleItems.AddRange(CreateItems());
        db.SaveChanges();
    }

    // Asenkron yol: uygulama açılışındaki MigrateAsync bunu çağırır.
    public static async Task SeedAsync(DbContext context, bool storeManagementPerformed, CancellationToken ct)
    {
        var db = (AppDbContext)context;
        var countOfSimpleItems = await db.SampleItems.CountAsync(ct);
        Console.WriteLine($"SampleItem tablosunda {countOfSimpleItems} kayıt var.");
        if (countOfSimpleItems > 0)
            return;

        db.SampleItems.AddRange(CreateItems());
        await db.SaveChangesAsync(ct);
    }
}