namespace MatchForecast.Api.Options;

public sealed class AiOptions
{
    public const string Section = "Ai";

    // Kullanılacak sağlayıcı: "Gemini" veya "Claude"; değiştirince API yeniden başlatılmalı.
    public string Provider { get; set; } = "Gemini";
    public int MaxTokens { get; set; } = 8000;
    public int CacheMinutes { get; set; } = 30;

    // Sondaki "/" zorunlu: HttpClient göreli yolu bu adrese ekler.
    public AiProviderOptions Gemini { get; set; } = new()
    {
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
        Model = "gemini-3.6-flash"
    };

    public AiProviderOptions Claude { get; set; } = new()
    {
        BaseUrl = "https://api.anthropic.com/",
        Model = "claude-sonnet-5-5"
    };
}

public sealed class AiProviderOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";

    // Hata mesajlarında hangi anahtarın kullanıldığını göstermek için ilk 4 ve son 4 karakter (ör. "AIza…x9Kf").
    public string MaskedApiKey() =>
        string.IsNullOrWhiteSpace(ApiKey) ? "(boş)"
        : ApiKey.Length <= 12 ? "****"
        : $"{ApiKey[..4]}…{ApiKey[^4..]}";
}