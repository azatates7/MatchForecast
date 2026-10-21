namespace MatchForecast.Api.Options;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    // Google Cloud Console > APIs & Services > Credentials > OAuth client ID (Web application). Boşsa Google girişi kapalıdır.
    public string GoogleClientId { get; set; } = "";

    // Şifre sıfırlama e-postasındaki bağlantının adresi; sonuna ?resetToken=... eklenir. İstekten (Origin) değil ayardan
    // alınır: aksi halde saldırgan, kurbana kendi sitesine giden ve token'ı ele geçiren bir bağlantı gönderttirebilirdi.
    public string PasswordResetUrl { get; set; } = "http://localhost:4200/"; // Angular url

    public int PasswordResetTokenMinutes { get; set; } = 30;
}