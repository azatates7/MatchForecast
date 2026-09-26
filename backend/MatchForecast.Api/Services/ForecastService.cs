using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services;

public sealed class ForecastService(
    IOddsProvider odds,
    IForecastAiClient ai,
    IMemoryCache cache,
    IOptions<AiOptions> aiOptions,
    ILogger<ForecastService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public async Task<ForecastResult> GetForecastAsync(int fixtureId, bool refresh, CancellationToken ct)
    {
        var key = $"forecast:{fixtureId}";
        if (!refresh && cache.TryGetValue(key, out ForecastResult? cached) && cached is not null)
            return cached;

        var match = await odds.GetMatchAsync(fixtureId, ct);
        if (match is null)
        {
            logger.LogWarning("Fixture {FixtureId} not found.", fixtureId);
            throw new ForecastException("Maç bulunamadı.", StatusCodes.Status404NotFound);
        }

        var matchOdds = await odds.GetOddsAsync(fixtureId, ct);
        if (matchOdds is null || matchOdds.Markets.Count == 0)
        {
            logger.LogWarning("No odds available for fixture {FixtureId}.", fixtureId);
            throw new ForecastException("Bu maç için henüz oran yayınlanmamış.", StatusCodes.Status404NotFound);
        }

        var userPrompt = ForecastPrompt.BuildUser(match, matchOdds, out var index);
        var raw = await ai.CompleteAsync(ForecastPrompt.System, userPrompt, ct);

        var parsed = Parse(raw);
        var predictions = (parsed.Predictions ?? [])
            .Where(p => p.OptionId is not null && index.ContainsKey(p.OptionId))
            .DistinctBy(p => p.OptionId)
            .OrderByDescending(p => p.Confidence)
            .Take(ForecastPrompt.PredictionCount)
            .Select((p, i) =>
            {
                var (market, option) = index[p.OptionId!];
                return new Prediction(
                    i + 1,
                    string.IsNullOrWhiteSpace(p.Label) ? $"{market.Name}: {option.Value}" : p.Label,
                    market.Name,
                    option.Value,
                    option.Odd,
                    ForecastPrompt.ImpliedProbability(option.Odd),
                    (int)Math.Clamp(Math.Round(p.Confidence), 0, 100),
                    p.Reasoning ?? "");
            })
            .ToList();

        if (predictions.Count == 0)
        {
            logger.LogWarning("AI returned no valid options for fixture {Id}. Raw: {Raw}", fixtureId, raw);
            throw new ForecastException("Yapay zeka geçerli bir seçenek döndürmedi, tekrar deneyin.");
        }

        var result = new ForecastResult(match, matchOdds.Bookmaker, parsed.Summary ?? "", predictions, DateTimeOffset.Now);
        cache.Set(key, result, TimeSpan.FromMinutes(aiOptions.Value.CacheMinutes));
        return result;
    }

    private AiResponse Parse(string raw)
    {
        // Model kurala uymayıp ```json bloğu veya ön metin eklerse ilk { ... son } aralığını al.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            logger.LogWarning("AI response does not contain valid JSON bounds. Raw: {Raw}", raw);
            throw new ForecastException("Yapay zeka cevabı JSON içermiyor.");
        }

        try
        {
            return JsonSerializer.Deserialize<AiResponse>(raw[start..(end + 1)], JsonOpts)
                   ?? throw new JsonException("empty");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "AI JSON parse failed. Raw: {Raw}", raw);
            throw new ForecastException("Yapay zeka cevabı okunamadı, tekrar deneyin.");
        }
    }

    private sealed record AiResponse(string? Summary, List<AiPrediction>? Predictions);
    private sealed record AiPrediction(string? OptionId, string? Label, double Confidence, string? Reasoning);
}
