using MatchForecast.Data.Configuration;
using MatchForecast.Data.Security;
using MatchForecast.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Services.Settings;

/*
 * Ayarları veritabanından (app_settings) yükler. Servisler kaydedilmeden ÖNCE çalışır; böylece IOptions sınıfları ve
 * ValidateOnStart kontrolleri değişmeden DB'deki değerleri görür, yeni eklenen ayarlar için yeniden başlatma gerekmez.
 *
 * Ortamdan (env / user-secrets) yalnızca iki değer okunur, çünkü ikisi de DB'ye bağlanmadan / DB'yi okumadan önce gerekir:
 *   - ConnectionStrings:Supabase  -> veritabanı bağlantısı (şifre dahil)
 *   - AppSettings:EncryptionKey   -> şifreli ayarları çözen anahtar (DB'de durursa şifrelemenin anlamı kalmaz)
 * Logging ve AllowedHosts da dosyada kalır: loglama DB'den önce başlar.
 *
 * Sıra: configuration'a en son eklendiği için DB değerleri appsettings.json, user-secrets ve env'deki değerleri ezer.
 * Ayar değişiklikleri (API ile) uygulama yeniden başlatılınca geçerli olur.
 */
public static class DatabaseSettings
{
    public const string TargetProject = "MatchForecast.Api";
    public const string EncryptionKeyName = "AppSettings:EncryptionKey";

    // DB'ye taşınan bölümler.
    private static readonly string[] Sections =
        ["OddsProvider", "Ai", "Smtp", "Jwt", "Admin", "Redis", "Forecast", "RateLimiting", "Cors", "Auth"];

    // Bu anahtarlar DB'de şifreli saklanır ve API'den hiçbir zaman açık metin dönmez.
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "OddsProvider:ApiKey",
        "Ai:Gemini:ApiKey",
        "Ai:Claude:ApiKey",
        "Ai:Ollama:ApiKey",
        "Smtp:Password",
        "Jwt:SecretKey",
        "Admin:Password"
    };

    /// <summary>Ayarları DB'den configuration'a ekler. Atlandıysa (bağlantı yok, testler, dotnet ef) null döner.</summary>
    public static async Task<IAppSettingsCrypto?> LoadAsync(WebApplicationBuilder builder, NLog.Logger logger)
    {
        // dotnet ef (migrations add vb.) Program.cs'i çalıştırır; o sırada DB'ye bağlanılmamalı ve migration uygulanmamalı.
        if (EF.IsDesignTime)
            return null;

        var connectionString = builder.Configuration.GetConnectionString("Supabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.Warn("ConnectionStrings:Supabase boş; ayarlar yalnızca appsettings.json / user-secrets / env'den okunuyor.");
            return null;
        }

        var key = builder.Configuration[EncryptionKeyName];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"{EncryptionKeyName} ayarlı değil. 32 byte'lık base64 bir anahtarı user-secrets'a veya ortam değişkenine " +
                "(AppSettings__EncryptionKey) ekleyin. Bu anahtar kaybolursa şifreli ayarlar çözülemez; güvenli bir yerde saklayın.");

        var crypto = new AesGcmAppSettingsCrypto(key);
        var environment = builder.Environment.EnvironmentName;

        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.UseDatabase(options, connectionString);
        await using var db = new AppDbContext(options.Options);

        // app_settings tablosu okunmadan önce var olmalı. Rol / örnek veri seed'i (UseAsyncSeeding) da burada çalışır.
        await db.Database.MigrateAsync();

        // DB'de olmayan anahtarlar şu anki değerlerinden (dosya + user-secrets + env) kopyalanır. İlk çalıştırmada hepsi.
        var currentValues = Sections.SelectMany(section => builder.Configuration.GetSection(section).AsEnumerable());
        var added = await AppSettingSeeder.SeedAsync(db, crypto, environment, TargetProject, currentValues, SecretKeys);
        if (added.Count > 0)
            logger.Info("{Count} ayar veritabanına eklendi ({Environment}): {Keys}", added.Count, environment, string.Join(", ", added));

        var settings = await db.AppSettings.AsNoTracking()
            .Where(x => x.Environment == environment && x.TargetProject == TargetProject && !x.IsDeleted)
            .ToListAsync();

        builder.Configuration.AddInMemoryCollection(settings.Select(s =>
            new KeyValuePair<string, string?>(s.Key, s.IsEncrypted ? crypto.Decrypt(s.Value) : s.Value)));

        logger.Info("{Count} ayar veritabanından yüklendi ({Environment}).", settings.Count, environment);
        return crypto;
    }
}