using System.Text.Json;

namespace MatchForecast.Api.Services.Forecast;

public interface IForecastAiClient
{
    /// <summary>Sistem ve kullanıcı prompt'unu gönderir; modelin düz metin cevabını ve token kullanımını döner.</summary>
    Task<AiCompletion> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

/*
 * Modelin cevabı ve sağlayıcının bildirdiği token sayıları (bildirmezse null).
 * OutputTokens, düşünme (thinking) token'larını da içerir: sağlayıcılar onları çıktı olarak faturalar.
 */
public sealed record AiCompletion(string Text, int? InputTokens = null, int? OutputTokens = null)
{
    // Sağlayıcı cevabındaki usage nesnesinden tam sayı alanı okur; alan yoksa veya sayı değilse null.
    public static int? ReadInt(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;
}