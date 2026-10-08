using System.Net.Http.Json;
using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services;

// Google Gemini API (generateContent) istemcisi.
public sealed class GeminiForecastClient(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<GeminiForecastClient> logger) : IForecastAiClient
{
    private readonly AiProviderOptions _opt = options.Value.Gemini;
    private readonly int _maxTokens = options.Value.MaxTokens;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
        {
            logger.LogError("Ai:Gemini:ApiKey configuration is missing or empty.");
            throw new ForecastException("Ai:Gemini:ApiKey tanımlı değil. user-secrets ile ekleyin.",
                StatusCodes.Status500InternalServerError);
        }

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { maxOutputTokens = _maxTokens }
        };

        // BaseUrl ".../v1beta/" olmalı; istek ".../v1beta/models/{model}:generateContent" adresine gider.
        var (status, raw) = await PostWithRetryAsync($"models/{_opt.Model}:generateContent", body, ct);

        if (status is < 200 or >= 300)
        {
            logger.LogError("Gemini API call failed with status code {StatusCode}. ApiKey: {ApiKey}. Raw response: {Raw}", status, _opt.MaskedApiKey(), raw);
            throw status == StatusCodes.Status503ServiceUnavailable
                ? new ForecastException("AI servisi şu an yoğun. Birkaç dakika sonra tekrar deneyin.", StatusCodes.Status503ServiceUnavailable)
                : new ForecastException($"AI servisi (Gemini, anahtar: {_opt.MaskedApiKey()}) HTTP {status}: {Truncate(raw, 300)}");
        }

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            logger.LogError("Gemini API returned no candidates. Raw response: {Raw}", raw);
            throw new ForecastException("AI servisi yanıt üretmedi (içerik filtresi veya boş yanıt).");
        }

        var candidate = candidates[0];
        var text = candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts)
            ? string.Concat(parts.EnumerateArray()
                .Where(p => p.TryGetProperty("text", out _)
                            && !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
                .Select(p => p.GetProperty("text").GetString()))
            : "";

        if (string.IsNullOrWhiteSpace(text))
        {
            var reason = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : "UNKNOWN";
            logger.LogError("Gemini API returned empty text. FinishReason: {Reason}. Raw response: {Raw}", reason, raw);
            throw new ForecastException(reason == "MAX_TOKENS"
                ? "AI yanıtı token limitine takıldı. Ai:MaxTokens değerini artırın."
                : $"AI servisi boş yanıt döndü (finishReason: {reason}).");
        }

        return text;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private async Task<(int Status, string Raw)> PostWithRetryAsync(string url, object body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var res = await http.PostAsJsonAsync(url, body, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);
            var status = (int)res.StatusCode;

            // Sadece geçici sunucu hatalarında tekrar dene
            if (status is not (500 or 503 or 504) || attempt == RetryDelays.Length)
                return (status, raw);

            logger.LogWarning("Gemini API returned {Status}, retrying in {Delay}s (attempt {Attempt})",
                status, RetryDelays[attempt].TotalSeconds, attempt + 1);
            await Task.Delay(RetryDelays[attempt], ct);
        }
    }
}