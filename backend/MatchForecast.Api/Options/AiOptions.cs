namespace MatchForecast.Api.Options;

public sealed class AiOptions
{
    public const string Section = "Ai";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.6-flash";
    public int MaxTokens { get; set; } = 2000;
    public int CacheMinutes { get; set; } = 30;
}
