namespace MatchForecast.Api.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    // HS256 için en az 32 byte (256 bit) olmalı; Program.cs'te ValidateOnStart ile kontrol edilir.
    public string SecretKey { get; set; } = "";
    public string Issuer { get; set; } = "MatchForecast.Api";
    public string Audience { get; set; } = "MatchForecast.Client";

    // Access token ömrü. Refresh token olduğu için kısa tutulabilir.
    public int ExpiryMinutes { get; set; } = 60;

    // Refresh token ömrü; bu süre boyunca kullanıcı tekrar şifre girmeden oturumunu yenileyebilir.
    public int RefreshTokenExpiryDays { get; set; } = 7;
}