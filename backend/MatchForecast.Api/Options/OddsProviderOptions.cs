namespace MatchForecast.Api.Options;

public sealed class OddsProviderOptions
{
    public const string Section = "OddsProvider";

    public string BaseUrl { get; set; } = "https://v3.football.api-sports.io/";
    public string ApiKey { get; set; } = string.Empty;
    public string Timezone { get; set; } = "Europe/Istanbul";
    public int CacheMinutes { get; set; } = 30;
}