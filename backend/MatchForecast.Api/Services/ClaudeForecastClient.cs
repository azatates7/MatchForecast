using System.Net.Http.Json;
using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services;

// Anthropic Messages API istemcisi; Ai:Provider "Claude" olduğunda kullanılır.
public sealed class ClaudeForecastClient(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<ClaudeForecastClient> logger) : IForecastAiClient
{
    private readonly AiProviderOptions _opt = options.Value.Claude;
    private readonly int _maxTokens = options.Value.MaxTokens;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
        {
            logger.LogError("Ai:Claude:ApiKey configuration is missing or empty.");
            throw new ForecastException("Ai:Claude:ApiKey tanımlı değil. user-secrets ile ekleyin.",
                StatusCodes.Status500InternalServerError);
        }

        var body = new
        {
            model = _opt.Model,
            max_tokens = _maxTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } }
        };

        using var res = await http.PostAsJsonAsync("v1/messages", body, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogError("Claude API call failed with status code {StatusCode}. ApiKey: {ApiKey}. Raw response: {Raw}", (int)res.StatusCode, _opt.MaskedApiKey(), raw);
            throw new ForecastException($"AI servisi (Claude, anahtar: {_opt.MaskedApiKey()}) HTTP {(int)res.StatusCode}: {Truncate(raw, 300)}");
        }

        using var doc = JsonDocument.Parse(raw);
        return string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}