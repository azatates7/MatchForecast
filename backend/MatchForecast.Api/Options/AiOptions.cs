namespace MatchForecast.Api.Options;

public sealed class AiOptions
{
    public const string Section = "Ai";

    // Kullanılacak sağlayıcı: "Gemini", "Claude" veya "Ollama"; değiştirince API yeniden başlatılmalı.
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

    // Yerel (veya uzak) Ollama sunucusu; bulut sağlayıcılarından farklı ayarları olduğu için ayrı sınıf.
    public OllamaOptions Ollama { get; set; } = new();
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

/*
 * Ollama ayarları (Ai:Ollama).
 * - BaseUrl: Ollama varsayılan olarak http://localhost:11434 adresinde dinler. API Docker içinde çalışıyorsa
 *   "http://host.docker.internal:11434/" kullanılmalı. Sondaki "/" zorunlu.
 * - Model: Önceden "ollama pull <model>" ile indirilmiş olmalı; yoksa Ollama 404 döner.
 * - ApiKey: Yerel Ollama anahtar istemez. Ollama Cloud veya önünde kimlik doğrulamalı bir reverse proxy varsa
 *   doldurulur ve "Authorization: Bearer {ApiKey}" olarak gönderilir.
 * - TimeoutSeconds: Yerel modeller (özellikle CPU'da) bulut API'lerinden çok daha yavaştır; 90 sn yetmeyebilir.
 * - ContextLength (num_ctx): Ollama, bağlam penceresini aşan prompt'u HATA VERMEDEN baştan keser. Oranlı maçlarda
 *   prompt 400 seçeneğe kadar çıkabildiği için varsayılan pencere (birkaç bin token) yetmez; sistem prompt'u
 *   kesilirse model JSON kuralını "görmez". Değer büyüdükçe RAM/VRAM kullanımı artar.
 */
public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434/";
    public string Model { get; set; } = "qwen3:8b";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 300;
    public int ContextLength { get; set; } = 16384;
}