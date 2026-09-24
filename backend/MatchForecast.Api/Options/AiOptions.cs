namespace MatchForecast.Api.Options;

public sealed class AiOptions
{
    public const string Section = "Ai";

    public string BaseUrl { get; set; } = "https://api.anthropic.com/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-5";
    public int MaxTokens { get; set; } = 2000;
    public int CacheMinutes { get; set; } = 30;
}
