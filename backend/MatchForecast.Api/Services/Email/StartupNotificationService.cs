using System.Reflection;
using System.Text;
using MatchForecast.Api.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Email;

/*
 * Uygulama her başladığında yöneticiye "uygulama başlatıldı" maili gönderir.
 *
 * Neden ApplicationStarted beklenir? Hosted service'ler Kestrel portu dinlemeye başlamadan önce çalışır. Mail hemen
 * atılsaydı, port çakışması gibi bir nedenle uygulama başlarken çökse bile "başlatıldı" maili gitmiş olurdu.
 * ApplicationStarted, sunucu istek kabul etmeye hazır olduğunda tetiklenir; ayrıca dinlenen adresler (URL) o anda bellidir.
 *
 * Gönderim hatası uygulamayı durdurmaz, sadece loglanır: SMTP sorunu yüzünden API'nin kapanması istenmez.
 * Mail EmailErrorNotifier'ın kuyruğuna değil doğrudan IEmailSender'a gider; o kuyruk hata bildirimleri ve throttle için.
 */
public sealed class StartupNotificationService(
    IEmailSender emailSender,
    IOptions<SmtpOptions> smtpOptions,
    IOptions<AiOptions> aiOptions,
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    IServer server,
    ILogger<StartupNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = smtpOptions.Value;
        if (!opt.Enabled || !opt.SendStartupNotification)
            return;

        // ApplicationStarted bir CancellationToken; tetiklenmesini Task'a çevirip bekliyoruz. Uygulama o ana kadar kapanırsa iptal edilir.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            await started.Task;
        }

        try
        {
            var subject = $"[MatchForecast][{environment.EnvironmentName}] Uygulama başlatıldı - {Environment.MachineName}";
            await emailSender.SendAsync(opt.AdminEmails, subject, BuildBody(), isHtml: false, stoppingToken);
            logger.LogInformation("Startup notification e-mail sent to admin.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Startup notification e-mail could not be sent. Host: {Host}:{Port}", opt.Host, opt.Port);
        }
    }

    private string BuildBody()
    {
        // Testlerde (TestServer) adres özelliği olmayabilir; null ise "(bilinmiyor)" yazılır.
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "(bilinmiyor)";

        var sb = new StringBuilder();
        sb.AppendLine("MatchForecast API başlatıldı ve istek kabul etmeye hazır.");
        sb.AppendLine();
        sb.AppendLine($"Zaman       : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Ortam       : {environment.EnvironmentName}");
        sb.AppendLine($"Sunucu      : {Environment.MachineName}");
        sb.AppendLine($"Adresler    : {(addresses is { Count: > 0 } ? string.Join(", ", addresses) : "(bilinmiyor)")}");
        sb.AppendLine($"Sürüm       : {version}");
        sb.AppendLine($"AI sağlayıcı: {aiOptions.Value.Provider}");
        sb.AppendLine($".NET        : {Environment.Version}");
        sb.AppendLine($"İşlem Id    : {Environment.ProcessId}");
        return sb.ToString();
    }
}