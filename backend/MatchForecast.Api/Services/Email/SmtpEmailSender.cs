using System.Net;
using System.Net.Mail;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Email;

/*
 * System.Net.Mail ile SMTP gönderimi; ek NuGet paketi gerektirmez.
 * Gmail'in 587 portu STARTTLS kullanır ve SmtpClient bunu EnableSsl=true ile destekler. 465 portu (implicit TLS)
 * SmtpClient tarafından desteklenmez; o port gerekirse MailKit'e geçilmelidir.
 * SmtpClient thread-safe değildir ve aynı anda tek gönderim yapabilir; bu yüzden her gönderimde yenisi oluşturulur.
 */
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _opt = options.Value;

    public async Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml, CancellationToken ct)
    {
        var from = string.IsNullOrWhiteSpace(_opt.FromAddress) ? _opt.Username : _opt.FromAddress;

        using var message = new MailMessage
        {
            From = new MailAddress(from, _opt.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = isHtml,
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8
        };
        foreach (var address in to.Where(a => !string.IsNullOrWhiteSpace(a)))
            message.To.Add(address);

        using var client = new SmtpClient(_opt.Host, _opt.Port)
        {
            EnableSsl = _opt.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_opt.Username, _opt.Password),
            Timeout = _opt.TimeoutSeconds * 1000
        };

        await client.SendMailAsync(message, ct);
    }
}