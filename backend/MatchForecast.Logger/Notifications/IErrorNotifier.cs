namespace MatchForecast.Logger.Notifications;

/*
 * Hata bildirimi sözleşmesi. Logger projesi yalnızca "bir hata oldu, haber ver" der; nasıl haber verileceğini
 * (SMTP, Slack, Teams...) bilmez. Uygulaması Api projesindedir (EmailErrorNotifier). Böylece Logger projesine
 * SMTP ayarları/bağımlılıkları sızmaz ve ileride başka kanal eklemek için middleware'e dokunmak gerekmez.
 */
public interface IErrorNotifier
{
    // Bloklamaz: bildirimi kuyruğa atar ve hemen döner; e-posta gönderimi HTTP cevabını geciktirmemeli.
    void Notify(ErrorNotification notification);
}

/*
 * Bildirim içeriği. HttpContext'in kendisi değil, gerekli alanların kopyası taşınır; çünkü gönderim arka planda,
 * istek bittikten sonra yapılır ve o noktada HttpContext artık geçerli değildir.
 */
public sealed record ErrorNotification(
    DateTimeOffset OccurredAt,
    string Method,
    string Path,
    string? QueryString,
    int StatusCode,
    string Message,
    string ExceptionType,
    string? StackTrace,
    string TraceId);