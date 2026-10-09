namespace MatchForecast.Api.Options;

/*
 * SMTP ayarları (Smtp bölümü). Varsayılanlar Gmail içindir: smtp.gmail.com, 587 portu ve STARTTLS (EnableSsl=true).
 * - Username: Gmail adresi (ör. ornek@gmail.com).
 * - Password: Gmail hesap şifresi DEĞİL, 16 haneli "Uygulama Şifresi" (App Password). Google normal şifreyle
 *   SMTP girişine izin vermez; hesapta 2 Adımlı Doğrulama açık olmalı. user-secrets'ta tutulmalı, appsettings'e yazılmamalı.
 * - FromAddress: Boşsa Username kullanılır. Gmail, gönderen adresini oturum açılan hesapla (veya hesapta tanımlı
 *   bir alias ile) değiştirir; farklı bir adres yazmak işe yaramaz.
 * - AdminEmails: Bildirimin gideceği adres(ler).
 * - ThrottleMinutes: Aynı hata (aynı status + exception tipi + mesaj) bu süre içinde tekrar olursa yeniden mail atılmaz.
 *   Örneğin Gemini 10 dk boyunca 503 dönerse, popüler maç toplu tahmini onlarca mail üretmez.
 * - SendStartupNotification: true ise uygulama her ayağa kalktığında AdminEmails'e "uygulama başlatıldı" maili gider
 *   (Enabled=false ise gönderilmez). Beklenmedik yeniden başlatmaları (crash, deploy, sunucu reboot) fark etmeyi sağlar.
 */
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "MatchForecast API";
    public string[] AdminEmails { get; set; } = [];
    public int ThrottleMinutes { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 30;
    public bool SendStartupNotification { get; set; } = true;
}