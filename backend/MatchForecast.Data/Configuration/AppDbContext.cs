using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MatchForecast.Data.Configuration;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /*
     * Supabase "public" şemasını Data API (REST) ile dışarı açar; RLS kapalı bir tabloya anon key ile erişilebilir.
     * Tablolar ayrı şemada tutulur, böylece yalnızca bu API (connection string) üzerinden erişilir.
     */
    public const string Schema = "matchforecast";

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    // DB'de UTC (timestamptz) kalır; entity'de İstanbul saati. Yazarken UTC'ye, okurken İstanbul'a çevrilir.
    private static readonly ValueConverter<DateTime, DateTime> IstanbulTime = new(
        v => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), Istanbul),
        v => TimeZoneInfo.ConvertTimeFromUtc(v, Istanbul));

    public DbSet<SampleItem> SampleItems => Set<SampleItem>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<GroupRole> GroupRoles => Set<GroupRole>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // Program.cs (DI kaydı) ve açılıştaki ayar yükleyici aynı Npgsql ayarlarını kullansın.
    public static void UseDatabase(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Migrate()/MigrateAsync() sonunda çalışır. Seed bağlantısı Data projesinde kalsın, Program.cs bilmesin.
        optionsBuilder
            .UseSeeding((context, performed) =>
            {
                SampleItemSeed.Seed(context, performed);
                RoleSeed.Seed(context, performed);
            })
            .UseAsyncSeeding(async (context, performed, ct) =>
            {
                await SampleItemSeed.SeedAsync(context, performed, ct);
                await RoleSeed.SeedAsync(context, performed, ct);
            });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SampleItem>(e =>
        {
            e.ToTable("sample_items");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.UserName).HasMaxLength(50).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasMaxLength(250);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Group>(e =>
        {
            e.ToTable("groups");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(250);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.Name).IsUnique();
        });

        // Ara tablolar: bileşik PK aynı atamanın iki kez yapılmasını engeller; kullanıcı/rol/grup silinince atamalar da silinir.
        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.Property(x => x.AssignedAt).HasDefaultValueSql("now()");
            e.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroup>(e =>
        {
            e.ToTable("user_groups");
            e.HasKey(x => new { x.UserId, x.GroupId });
            e.Property(x => x.AssignedAt).HasDefaultValueSql("now()");
            e.HasOne(x => x.User).WithMany(x => x.UserGroups).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany(x => x.UserGroups).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupRole>(e =>
        {
            e.ToTable("group_roles");
            e.HasKey(x => new { x.GroupId, x.RoleId });
            e.Property(x => x.AssignedAt).HasDefaultValueSql("now()");
            e.HasOne(x => x.Group).WithMany(x => x.GroupRoles).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany(x => x.GroupRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserLogin>(e =>
        {
            e.ToTable("user_logins");
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.ProviderKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.ProviderDisplayName).HasMaxLength(100);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            // Aynı Google/GitHub hesabı yalnızca bir kullanıcıya bağlanabilir.
            e.HasIndex(x => new { x.Provider, x.ProviderKey }).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.Logins).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            // SHA-256 hex = 64 karakter. Refresh isteğinde token bu kolondan aranır, bu yüzden unique index.
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.PasswordResetTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("app_settings");
            e.Property(x => x.Key).HasMaxLength(200).IsRequired();
            e.Property(x => x.Value).IsRequired(); // Şifreli değerler uzun olabilir; uzunluk sınırı yok (text).
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.TargetProject).HasMaxLength(100).IsRequired();
            e.Property(x => x.Environment).HasMaxLength(50).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.Environment, x.TargetProject, x.Key }).IsUnique();
        });

        /*
         * Tüm entity'ler için ortak kurallar; her property'ye ayrı ayrı yazılmasın diye en sonda uygulanır:
         * - Kolon adları snake_case (CreatedAt -> created_at). HasColumnName ile özel ad verilmiş kolonlara dokunulmaz.
         * - DateTime / DateTime? alanları İstanbul saatine çevrilir (DB'de UTC kalır).
         */
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.GetColumnName() == property.Name)
                property.SetColumnName(ToSnakeCase(property.Name));

            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                property.SetValueConverter(IstanbulTime);
        }
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c)
            ? "_" + char.ToLowerInvariant(c)
            : char.ToLowerInvariant(c).ToString()));
}