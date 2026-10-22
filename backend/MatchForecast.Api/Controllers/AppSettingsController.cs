using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Security;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Controllers;

/*
 * Veritabanındaki uygulama ayarları (yalnızca Admin). Değişiklikler uygulama yeniden başlatılınca geçerli olur Ayarlar açılışta bir kez configuration'a yüklenir ve IOptions değerleri o an sabitlenir.
 * Yeni anahtar eklemek için: değeri user-secrets / env'e yazıp uygulamayı yeniden başlatmak yeterli (seed eksikleri ekler).
 */
[ApiController]
[Route("api/app-settings")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AppSettingsController(AppDbContext db, IWebHostEnvironment hostEnvironment) : ControllerBase
{
    private const string Mask = "********";

    /// <summary>Ayarları listeler. environment verilmezse uygulamanın çalıştığı ortam kullanılır. Şifreli değerler maskelenir.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AppSettingResponse>>> GetAll(
        [FromQuery] string? environment, [FromQuery] string? targetProject, CancellationToken ct)
    {
        var env = string.IsNullOrWhiteSpace(environment) ? hostEnvironment.EnvironmentName : environment;
        var query = db.AppSettings.AsNoTracking().Where(x => x.Environment == env);
        if (!string.IsNullOrWhiteSpace(targetProject))
            query = query.Where(x => x.TargetProject == targetProject);

        var items = await query.OrderBy(x => x.TargetProject).ThenBy(x => x.Key).ToListAsync(ct);
        return items.Select(ToResponse).ToList();
    }

    /// <summary>
    /// reveal=true ise şifreli değer çözülmüş haliyle döner (yalnızca Admin)
    /// </summary>
[HttpGet("{id:guid}")]
public async Task<ActionResult<AppSettingResponse>> Get(
    Guid id, [FromQuery] bool reveal, [FromServices] IAppSettingsCrypto crypto, CancellationToken ct)
{
    var setting = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    if (setting is null)
        return NotFound();

    var response = ToResponse(setting);
    return reveal && setting.IsEncrypted
        ? response with { Value = crypto.Decrypt(setting.Value) }
        : response;
}

    /// <summary>
    /// Değeri, açıklamayı veya şifreleme durumunu günceller. Value boş gönderilirse mevcut değer korunur (ör. yalnızca açıklama değişiyorsa gizli değeri tekrar yazmak gerekmez). Yeniden başlatma sonrası geçerli olur.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, UpdateAppSettingRequest request, [FromServices] IAppSettingsCrypto crypto, CancellationToken ct)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (setting is null)
            return NotFound();

        var plainValue = request.Value ?? (setting.IsEncrypted ? crypto.Decrypt(setting.Value) : setting.Value);

        setting.Value = request.IsEncrypted ? crypto.Encrypt(plainValue) : plainValue;
        setting.IsEncrypted = request.IsEncrypted;
        setting.Description = request.Description;
        setting.UpdatedAt = DateTime.Now;
        setting.UpdatedBy = User.Identity?.Name;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Ayarı siler. Anahtar appsettings.json / user-secrets / env'de de tanımlıysa, sonraki açılışta oradaki değerle yeniden eklenir; yani silmek "varsayılan değere dön" anlamına gelir.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await db.AppSettings.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }

    private static AppSettingResponse ToResponse(AppSetting s) => new(
        s.Id, s.Key, s.IsEncrypted ? Mask : s.Value, s.IsEncrypted, s.Description,
        s.TargetProject, s.Environment, s.CreatedAt, s.UpdatedAt, s.UpdatedBy);
}