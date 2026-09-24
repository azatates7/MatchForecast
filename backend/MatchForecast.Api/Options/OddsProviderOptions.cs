namespace MatchForecast.Api.Options;

public sealed class OddsProviderOptions
{
    public const string Section = "OddsProvider";

    /// <summary>true ise API anahtarı olmadan örnek veriyle çalışır.</summary>
    public bool UseMock { get; set; } = true;
    public string BaseUrl { get; set; } = "https://v3.football.api-sports.io/";
    public string ApiKey { get; set; } = string.Empty;
    public string Timezone { get; set; } = "Europe/Istanbul";

    /// <summary>Boşsa, en çok market sunan bahisçi seçilir.</summary>
    public int? BookmakerId { get; set; }
    public int CacheMinutes { get; set; } = 10;
}
