namespace MatchForecast.Api.Services;

public interface IForecastAiClient
{
    /// <summary>Sistem ve kullanıcı prompt'unu gönderir, modelin düz metin cevabını döner.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}
