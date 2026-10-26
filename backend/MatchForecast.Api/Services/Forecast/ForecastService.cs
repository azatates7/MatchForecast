using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.ApiFootball;

namespace MatchForecast.Api.Services.Forecast;

/*
 * Tahmin akışı:
 * 1. Maç bilgisi (API-Football, cache'li).
 * 2. Model, metriklerin beklenen değerlerini JSON olarak verir.
 * 3. ProjectionBuilder değerleri doğrular, Poisson ile aralık ve olasılıkları hesaplar.
 * 4. PredictionSelector katalogdan tahmin listesini seçer.
 * Oran artık kullanılmaz; tahmin başına API-Football oran isteği yapılmaz.
 */
public sealed class ForecastService(
    IOddsProvider odds,
    IForecastAiClient ai,
    IRedisCacheService cache,
    IOptions<AiOptions> aiOptions,
    IOptions<ForecastOptions> forecastOptions,
    ILogger<ForecastService> logger)
{
    /*
     * Aynı maç için eşzamanlı cache miss'lerde AI bir kez çağrılsın (ör. ön ısıtma çalışırken kullanıcı aynı maça tıklarsa).
     * Sabit sayıda kilit (maç id'sine göre) kullanılır, maç başına nesne birikmez. Tek instance içinde geçerlidir.
     */
    private static readonly SemaphoreSlim[] FixtureLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public async Task<IReadOnlyList<ForecastResult>> GetPopularForecastsAsync(DateOnly date, int count, bool refresh, CancellationToken ct)
    {
        var popularMatches = await odds.GetPopularMatchesAsync(date, count, ct);
        var results = new List<ForecastResult>();

        foreach (var match in popularMatches)
        {
            try
            {
                var forecast = await GetForecastAsync(match.Id, refresh, ct);
                results.Add(forecast);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not generate forecast for popular match {Id} ({Home} vs {Away})", match.Id, match.HomeTeam, match.AwayTeam);
            }
        }

        return results;
    }

    public async Task<ForecastResult> GetForecastAsync(int fixtureId, bool refresh, CancellationToken ct)
    {
        // v2: eski (oranlı) formatta cache'lenmiş tahminler yeni modele uymaz; farklı anahtar onları yok sayar, 30 dk'da kendiliğinden silinirler.
        var key = $"forecast:v2:{fixtureId}";
        if (!refresh)
        {
            var cached = await cache.GetAsync<ForecastResult>(key);
            if (cached is not null)
                return cached;
        }

        var gate = FixtureLocks[(fixtureId & int.MaxValue) % FixtureLocks.Length];
        await gate.WaitAsync(ct);
        try
        {
            // Kilit beklenirken başka bir istek (ör. ön ısıtma) aynı tahmini üretip cache'e yazmış olabilir.
            if (!refresh)
            {
                var cached = await cache.GetAsync<ForecastResult>(key);
                if (cached is not null)
                    return cached;
            }

            return await GenerateAsync(fixtureId, key, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ForecastResult> GenerateAsync(int fixtureId, string key, CancellationToken ct)
    {
        var match = await odds.GetMatchAsync(fixtureId, ct);
        if (match is null)
        {
            logger.LogWarning("Fixture {FixtureId} not found.", fixtureId);
            throw new ForecastException("Maç bulunamadı.", StatusCodes.Status404NotFound);
        }

        var raw = await ai.CompleteAsync(ForecastPrompt.System, ForecastPrompt.BuildUser(match), ct);
        var response = Parse(raw);

        var opt = forecastOptions.Value;
        var projection = ProjectionBuilder.Build(response, opt, logger);
        var predictions = PredictionSelector.Select(projection, opt);

        if (predictions.Count == 0)
        {
            logger.LogWarning("No predictions could be selected for fixture {Id}. Raw: {Raw}", fixtureId, raw);
            throw new ForecastException("Yapay zeka geçerli bir tahmin üretmedi, tekrar deneyin.");
        }

        var result = new ForecastResult(match, response.Summary ?? "", projection.Outcomes, projection.Stats, predictions, DateTimeOffset.Now);
        await cache.SetAsync(key, result, TimeSpan.FromMinutes(aiOptions.Value.CacheMinutes));
        return result;
    }

    private AiStatsResponse Parse(string raw)
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
            return JsonSerializer.Deserialize<AiStatsResponse>(raw[start..(end + 1)], JsonOpts)
                   ?? throw new JsonException("empty");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "AI JSON parse failed. Raw: {Raw}", raw);
            throw new ForecastException("Yapay zeka cevabı okunamadı, tekrar deneyin.");
        }
    }
}