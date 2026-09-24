using System.Net.Http.Json;
using System.Text.Json;
using MatchForecast.Api.Models;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services;

/// <summary>Anthropic Messages API istemcisi. Başka bir sağlayıcı için IForecastAiClient'ı uygulamak yeterli.</summary>
public sealed class ClaudeForecastClient(HttpClient http, IOptions<AiOptions> options) : IForecastAiClient
{
    private readonly AiOptions _opt = options.Value;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
            throw new ForecastException("Ai:ApiKey tanımlı değil. appsettings veya user-secrets ile ekleyin.",
                StatusCodes.Status500InternalServerError);

        var body = new
        {
            model = _opt.Model,
            max_tokens = _opt.MaxTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } }
        };

        using var res = await http.PostAsJsonAsync("v1/messages", body, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new ForecastException($"AI servisi HTTP {(int)res.StatusCode}: {Truncate(raw, 300)}");

        using var doc = JsonDocument.Parse(raw);
        return string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
