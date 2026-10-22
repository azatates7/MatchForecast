using MatchForecast.Data.Entity.Common;

namespace MatchForecast.Data.Entity;

/// <summary>
/// Uygulama ayarı (appsettings.json yerine veritabanında). Açılışta, ilgili ortam ve proje için bütün kayıtlar configuration'a eklenir; IOptions<...> sınıfları ayarları dosyadan okuyormuş gibi kullanır. Aynı ortam + proje + anahtar yalnızca bir kez bulunabilir (unique index).
/// </summary>
public sealed class AppSetting : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Hiyerarşik anahtar: "Jwt:SecretKey", "Smtp:Password", dizilerde "Cors:AllowedOrigins:0".
    public string Key { get; set; } = null!;

    // IsEncrypted ise AES-GCM ile şifrelenmiş metin (base64), değilse düz değer.
    public string Value { get; set; } = null!;

    public bool IsEncrypted { get; set; }

    public string? Description { get; set; }

    // Ayarı kullanan proje, ör. "MatchForecast.Api". Aynı veritabanını kullanan başka bir servis kendi ayarlarını ayırabilir.
    public string TargetProject { get; set; } = null!;

    // ASP.NET Core ortam adı: "Development", "Production"... Her ortamın değerleri ayrı satırlardadır.
    public string Environment { get; set; } = null!;
}