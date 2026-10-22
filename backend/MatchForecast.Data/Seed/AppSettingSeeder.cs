using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Security;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Data.Seed;

/*
 * İlk değerler kod içine yazılmaz: çağıran, o anki configuration'dan (appsettings.json + user-secrets + env) okuduğu
 * değerleri verir. Böylece gizli değerler seed koduna, dolayısıyla repoya hiç girmez.
 * Yalnızca DB'de OLMAYAN anahtarlar eklenir; sonradan API ile güncellenmiş değerlere dokunulmaz. Boş değerler eklenmez
 * (ör. henüz girilmemiş bir API key); değer sonradan user-secrets/env'e girilip uygulama yeniden başlatılınca eklenir.
 */
public static class AppSettingSeeder
{
    public static async Task<IReadOnlyList<string>> SeedAsync(
        AppDbContext db,
        IAppSettingsCrypto crypto,
        string environment,
        string targetProject,
        IEnumerable<KeyValuePair<string, string?>> currentValues,
        IReadOnlySet<string> secretKeys,
        CancellationToken ct = default)
    {
        var existing = (await db.AppSettings
                .Where(x => x.Environment == environment && x.TargetProject == targetProject)
                .Select(x => x.Key)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = currentValues
            .Where(x => !string.IsNullOrEmpty(x.Value) && !existing.Contains(x.Key))
            .Select(x =>
            {
                var isSecret = secretKeys.Contains(x.Key);
                return new AppSetting
                {
                    Key = x.Key,
                    Value = isSecret ? crypto.Encrypt(x.Value!) : x.Value!,
                    IsEncrypted = isSecret,
                    Description = isSecret ? "Gizli değer (şifreli)." : null,
                    TargetProject = targetProject,
                    Environment = environment
                };
            })
            .ToList();

        if (missing.Count == 0)
            return [];

        db.AppSettings.AddRange(missing);
        await db.SaveChangesAsync(ct);
        return missing.Select(x => x.Key).ToList();
    }
}