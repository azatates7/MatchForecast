using System.Text;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Email;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Health;

/*
 * Bileşen durumlarını izler; durum değişikliğini ancak ConsecutiveChecks ardışık kontrolde aynı kalınca "onaylar".
 * Başlangıçta her bileşen Healthy kabul edilir: açılışta sorunlu olan bileşen de (onaylandıktan sonra) bildirilir.
 * Saf mantık; zamanlama ve mail HealthMonitorService'te.
 */
public sealed class HealthStateTracker(int consecutiveChecks)
{
    private readonly Dictionary<string, HealthStatus> _confirmed = new();
    private readonly Dictionary<string, (HealthStatus Status, int Count)> _candidate = new();

    public sealed record Change(string Name, HealthStatus From, HealthStatus To, string? Description);

    public IReadOnlyList<Change> Update(IReadOnlyDictionary<string, (HealthStatus Status, string? Description)> current)
    {
        var changes = new List<Change>();
        foreach (var (name, (status, description)) in current)
        {
            var confirmed = _confirmed.GetValueOrDefault(name, HealthStatus.Healthy);
            if (status == confirmed)
            {
                _candidate.Remove(name);
                continue;
            }

            var count = _candidate.TryGetValue(name, out var c) && c.Status == status ? c.Count + 1 : 1;
            if (count >= Math.Max(1, consecutiveChecks))
            {
                _confirmed[name] = status;
                _candidate.Remove(name);
                changes.Add(new Change(name, confirmed, status, description));
            }
            else
                _candidate[name] = (status, count);
        }
        return changes;
    }
}

/*
 * Uygulama içi sağlık izleme: /healthz/ready kontrollerini (veritabanı, Redis, Kafka) düzenli çalıştırır, bir bileşen
 * bozulunca ve düzelince yöneticiye mail atar. Redis ve Kafka bozulduğunda site çalışmaya devam ettiği için (fallback)
 * dışarıdan izleme bunu fark etmez; bu servis tam olarak o durumu bildirir. Mail yalnızca durum değişince gider, aynı
 * sorun için tekrar etmez. Sunucu/container tamamen kapanırsa bu servis de çalışmaz (dış izleme gerekir).
 */
public sealed class HealthMonitorService(
    HealthCheckService healthChecks,
    IEmailSender emailSender,
    IOptions<HealthMonitorOptions> options,
    IOptions<SmtpOptions> smtpOptions,
    IHostEnvironment environment,
    ILogger<HealthMonitorService> logger) : BackgroundService
{
    // Açılışta bağımlılıklar (Kafka, Redis) henüz hazır olmayabilir; ilk kontrol biraz beklenerek yapılır.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
            return;

        var tracker = new HealthStateTracker(opt.ConsecutiveChecks);
        var interval = TimeSpan.FromSeconds(Math.Max(30, opt.IntervalSeconds));
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                await CheckAsync(tracker, stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckAsync(HealthStateTracker tracker, CancellationToken ct)
    {
        try
        {
            var report = await healthChecks.CheckHealthAsync(c => c.Tags.Contains("ready"), ct);
            var current = report.Entries.ToDictionary(e => e.Key, e => (e.Value.Status, e.Value.Description));
            foreach (var change in tracker.Update(current))
            {
                logger.Log(change.To == HealthStatus.Healthy ? LogLevel.Information : LogLevel.Warning,
                    "Health of {Component} changed: {From} -> {To}. {Description}", change.Name, change.From, change.To, change.Description);
                await NotifyAsync(change, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Health monitor check failed.");
        }
    }

    private async Task NotifyAsync(HealthStateTracker.Change change, CancellationToken ct)
    {
        var smtp = smtpOptions.Value;
        if (!smtp.Enabled || smtp.AdminEmails.Length == 0)
            return;

        try
        {
            await emailSender.SendAsync(smtp.AdminEmails, Subject(change, environment.EnvironmentName), Body(change), isHtml: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Health notification e-mail could not be sent for {Component}.", change.Name);
        }
    }

    public static string Subject(HealthStateTracker.Change change, string environmentName) =>
        change.To == HealthStatus.Healthy
            ? $"[MatchForecast][{environmentName}] Düzeldi: {Name(change.Name)}"
            : $"[MatchForecast][{environmentName}] Sorun: {Name(change.Name)} {(change.To == HealthStatus.Unhealthy ? "çalışmıyor" : "kısmen çalışıyor")}";

    private static string Body(HealthStateTracker.Change change)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Bileşen : {Name(change.Name)}");
        sb.AppendLine($"Durum   : {change.From} -> {change.To}");
        sb.AppendLine($"Zaman   : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Sunucu  : {Environment.MachineName}");
        if (!string.IsNullOrWhiteSpace(change.Description))
            sb.AppendLine($"Açıklama: {change.Description}");
        sb.AppendLine();
        sb.AppendLine(change.To == HealthStatus.Healthy
            ? "Bileşen yeniden sağlıklı."
            : "Ayrıntılar API loglarında. Bileşen düzelince ayrıca bilgi verilir; aynı sorun için tekrar mail gönderilmez.");
        return sb.ToString();
    }

    private static string Name(string key) => key switch
    {
        "database" => "Veritabanı",
        "redis" => "Redis",
        "kafka" => "Kafka",
        _ => key
    };
}