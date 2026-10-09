namespace MatchForecast.Api.Services.Email;

// Genel amaçlı e-posta gönderici; hata bildirimi dışında (ör. ileride kullanıcı bildirimleri) da kullanılabilir.
public interface IEmailSender
{
    Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml, CancellationToken ct);
}