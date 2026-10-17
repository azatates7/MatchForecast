using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Data.Configuration;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /*
     * Supabase "public" şemasını Data API (REST) ile dışarı açar; RLS kapalı bir tabloya anon key ile erişilebilir.
     * Tablolar ayrı şemada tutulur, böylece yalnızca bu API (connection string) üzerinden erişilir.
     */
    public const string Schema = "matchforecast";

    public DbSet<SampleItem> SampleItems => Set<SampleItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Migrate()/MigrateAsync() sonunda çalışır. Seed bağlantısı Data projesinde kalsın, Program.cs bilmesin.
        optionsBuilder
            .UseSeeding(SampleItemSeed.Seed)
            .UseAsyncSeeding(SampleItemSeed.SeedAsync);
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SampleItem>(e =>
        {
            e.ToTable("sample_items");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });
    }
}