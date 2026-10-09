using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using MatchForecast.Api.Options;
using MatchForecast.Logger.Notifications;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Email;

/*
 * API hatalarını yöneticiye e-posta ile bildirir. Hem IErrorNotifier (kuyruğa yazan taraf) hem de BackgroundService
 * (kuyruktan okuyup gönderen taraf) olarak aynı singleton instance kullanılır.
 *
 * Neden kuyruk (Channel)? SMTP gönderimi 1-5 sn sürebilir ve başarısız olabilir. Middleware içinde await edilseydi
 * hata alan kullanıcı cevabı geç alırdı; her hata için Task.Run ile ayrı gönderim yapılsaydı bir hata dalgasında aynı anda
 * onlarca SMTP bağlantısı açılır, throttle kontrolü yarışa girer ve Gmail gönderim limitlerine takılınırdı. Kuyrukta
 * istek hiç beklemez, mailler tek okuyucu tarafından sırayla gönderilir; kuyruk dolarsa yeni bildirimler düşürülür
 * (SMTP çökse bile bellek şişmez). Uygulama kapanırken kuyrukta kalan bildirimler gönderilmez; hepsi zaten loglanmıştır.
 */
public sealed class EmailErrorNotifier(
    IEmailSender emailSender,
    IOptions<SmtpOptions> options,
    IHostEnvironment environment,
    ILogger<EmailErrorNotifier> logger) : BackgroundService, IErrorNotifier
{
    private readonly SmtpOptions _opt = options.Value;

    private readonly Channel<ErrorNotification> _queue = Channel.CreateBounded<ErrorNotification>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    // Hata anahtarı -> son mail zamanı. Aynı hatanın ThrottleMinutes içinde tekrar maillenmesini engeller.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new();

    public void Notify(ErrorNotification notification)
    {
        if (!_opt.Enabled)
            return;

        if (!_queue.Writer.TryWrite(notification))
            logger.LogWarning("Error notification queue is full; notification dropped. Path: {Path}", notification.Path);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!ShouldSend(notification))
                continue;

            try
            {
                await emailSender.SendAsync(_opt.AdminEmails, BuildSubject(notification), BuildBody(notification), isHtml: false, stoppingToken);
                logger.LogInformation("Error notification e-mail sent to admin. Status: {Status}, Path: {Path}", notification.StatusCode, notification.Path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Mail gönderilemediyse throttle kaydı silinir; aynı hata tekrar olursa yeniden denenir.
                _lastSent.TryRemove(ThrottleKey(notification), out _);
                logger.LogError(ex, "Error notification e-mail could not be sent. Host: {Host}:{Port}", _opt.Host, _opt.Port);
            }
        }
    }

    private bool ShouldSend(ErrorNotification n)
    {
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromMinutes(_opt.ThrottleMinutes);
        var key = ThrottleKey(n);

        // Kuyruğu tek okuyucu işlediği için oku-yaz arasında yarış yok.
        if (_lastSent.TryGetValue(key, out var last) && now - last < window)
        {
            logger.LogInformation("Error notification throttled (same error within {Minutes} min). Key: {Key}", _opt.ThrottleMinutes, key);
            return false;
        }

        _lastSent[key] = now;
        return true;
    }

    /*
     * Path anahtara bilerek dahil edilmez: /api/matches/1/forecast ile /api/matches/2/forecast aynı Gemini 503 hatasını
     * alıyorsa tek mail yeterli. Mesaj 200 karakterle sınırlanır; ham AI yanıtı içeren uzun mesajlar anahtarı şişirmesin.
     */
    private static string ThrottleKey(ErrorNotification n) =>
        $"{n.StatusCode}|{n.ExceptionType}|{(n.Message.Length <= 200 ? n.Message : n.Message[..200])}";

    private string BuildSubject(ErrorNotification n) =>
        $"[MatchForecast][{environment.EnvironmentName}] HTTP {n.StatusCode} - {n.Method} {n.Path}";

    private string BuildBody(ErrorNotification n)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MatchForecast API bir hata cevabı döndürdü.");
        sb.AppendLine();
        sb.AppendLine($"Zaman       : {n.OccurredAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Ortam       : {environment.EnvironmentName}");
        sb.AppendLine($"Sunucu      : {Environment.MachineName}");
        sb.AppendLine($"İstek       : {n.Method} {n.Path}{n.QueryString}");
        sb.AppendLine($"HTTP Status : {n.StatusCode}");
        sb.AppendLine($"TraceId     : {n.TraceId}");
        sb.AppendLine($"Exception   : {n.ExceptionType}");
        sb.AppendLine();
        sb.AppendLine("Mesaj:");
        sb.AppendLine(n.Message);
        sb.AppendLine();
        sb.AppendLine("Stack trace:");
        sb.AppendLine(string.IsNullOrWhiteSpace(n.StackTrace) ? "(yok)" : n.StackTrace);
        sb.AppendLine();
        sb.AppendLine($"Not: Aynı hata {_opt.ThrottleMinutes} dk içinde tekrar ederse yeni mail gönderilmez; ayrıntılar log dosyasındadır (TraceId ile aranabilir).");
        return sb.ToString();
    }
}