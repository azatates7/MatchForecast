using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Absences;

namespace MatchForecast.Api.Services.Forecast;

/*
 * Tahmin akışı:
 * 1. Maç bilgisi (API-Football, cache'li).
 * 2. Model, metriklerin beklenen değerlerini JSON olarak verir.
 * 3. ProjectionBuilder değerleri doğrular, Poisson ile aralık ve olasılıkları hesaplar.
 * 4. PredictionSelector katalogdan tahmin listesini seçer.
 * 5. Sonuç Redis'e (okuma için) ve veritabanına (kalıcı kayıt, IForecastStore) yazılır.
 * Oran artık kullanılmaz; tahmin başına API-Football oran isteği yapılmaz.
 */
public sealed class ForecastService(
    IOddsProvider odds,
    IForecastAiClient ai,
    IRedisCacheService cache,
    IForecastStore store,
    IAiUsageStore usageStore,
    IAbsenceResearcher absenceResearcher,
    IOptions<AiOptions> aiOptions,
    IOptions<AbsencesOptions> absenceOptions,
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
        /*
         * Sonucun biçimi ya da prompt'un içeriği değişince sürüm artırılır; eski anahtarlı kayıtlar yok sayılır, süreleri dolunca silinir.
         * v6: eksik oyuncular API-Football yerine maç günü resmi açıklamalardan (AbsenceReport); sonuç alanı Absences.
         * v5: sonuca MissingPlayers eklendi (v4 kayıtlarında bu alan yok, ekran eksik oyuncu bölümünü gösteremez).
         * v4: prompt'a eksik oyuncular eklendi. v3: iki dilli metinler.
         */
        var key = $"forecast:v6:{fixtureId}";
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
            throw new ForecastException(Lang.T("Maç bulunamadı.", "Match not found."), StatusCodes.Status404NotFound);
        }

        var absences = await GetAbsencesAsync(match, ct);
        var raw = (await CallAiAsync(fixtureId, ForecastPrompt.BuildUser(match, absences), ct)).Text;
        var response = Parse(raw);

        var opt = forecastOptions.Value;
        var projection = ProjectionBuilder.Build(response, opt, logger);
        var predictions = PredictionSelector.Select(projection, opt);

        if (predictions.Count == 0)
        {
            logger.LogWarning("No predictions could be selected for fixture {Id}. Raw: {Raw}", fixtureId, raw);
            throw new ForecastException(Lang.T("Yapay zeka geçerli bir tahmin üretmedi, tekrar deneyin.", "The AI did not produce a valid forecast, please try again."));
        }

        var result = new ForecastResult(match, LocalizedText.Of(response.Summary, response.SummaryEn), projection.Outcomes, projection.Stats, predictions, DateTimeOffset.Now, absences);
        await cache.SetAsync(key, result, ForecastCacheTtl(match, absences, DateTime.Now));
        await SaveAsync(result);
        return result;
    }

    // Fail-open: araştırma hata verirse (veya null dönerse) tahmin yine üretilir; prompt'ta "bilgi alınamadı" yazar.
    private async Task<AbsenceReport> GetAbsencesAsync(MatchSummary match, CancellationToken ct)
    {
        try
        {
            return await absenceResearcher.GetAsync(match, ct) ?? AbsenceReport.UnavailableReport();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Absences for fixture {FixtureId} could not be loaded; forecasting without them.", match.Id);
            return AbsenceReport.UnavailableReport();
        }
    }

    /*
     * Tahmin önbellek süresi (Ai:CacheMinutes) eksik oyuncu durumuna göre kısalır:
     * - Pending (maç günü değil): en geç maç gününün başında (00:00) biter; maç günü tahmin eksiklerle yeniden üretilir.
     * - Ready (maç günü): Absences:CacheMinutes'ı aşmaz; kadro bilgisi maça yaklaştıkça netleştiği için tahmin de tazelenir.
     */
    public TimeSpan ForecastCacheTtl(MatchSummary match, AbsenceReport absences, DateTime now)
    {
        var ttl = TimeSpan.FromMinutes(aiOptions.Value.CacheMinutes);
        if (absences.State == AbsenceReport.Pending)
        {
            var untilMatchDay = match.Kickoff.ToLocalTime().Date - now;
            ttl = untilMatchDay < ttl ? untilMatchDay : ttl;
        }
        else if (absences.State == AbsenceReport.Ready)
        {
            var refresh = TimeSpan.FromMinutes(absenceOptions.Value.CacheMinutes);
            ttl = refresh < ttl ? refresh : ttl;
        }
        return ttl < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : ttl;
    }

    /*
     * AI çağrısı ölçülür ve başarılı/başarısız her çağrı ai_usages'a yazılır (admin maliyet raporu).
     * İstemci iptali (OperationCanceledException) sağlayıcı hatası değildir, kaydedilmez.
     */
    private async Task<AiCompletion> CallAiAsync(int fixtureId, string userPrompt, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var completion = await ai.CompleteAsync(ForecastPrompt.System, userPrompt, ct);
            await RecordUsageAsync(fixtureId, completion, null, started);
            return completion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sağlayıcının gerçek kodu (ör. 429) kaydedilir; istemciye dönen kod farklı olabilir (429 → 503).
            var fe = ex as ForecastException;
            await RecordUsageAsync(fixtureId, null, fe?.UpstreamStatusCode ?? fe?.StatusCode, started);
            throw;
        }
    }

    // Fail-open: kullanım kaydı yazılamazsa tahmin akışı etkilenmez. CancellationToken.None: istemci kapansa da kayıt yazılsın.
    private async Task RecordUsageAsync(int fixtureId, AiCompletion? completion, int? failedStatusCode, long started)
    {
        try
        {
            await usageStore.RecordAsync(fixtureId, completion, failedStatusCode,
                System.Diagnostics.Stopwatch.GetElapsedTime(started), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI usage for fixture {FixtureId} could not be recorded.", fixtureId);
        }
    }

    /*
     * Kalıcı kayıt fail-open: veritabanı hatası kullanıcıya tahmin dönülmesini engellemez (Redis gibi).
     * CancellationToken.None: istemci bağlantıyı kapatsa da AI'a ödenen tahmin kaydedilsin.
     */
    private async Task SaveAsync(ForecastResult result)
    {
        try
        {
            await store.SaveAsync(result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Forecast for fixture {FixtureId} could not be saved to the database.", result.Match.Id);
        }
    }

    private AiStatsResponse Parse(string raw)
    {
        // Model kurala uymayıp ```json bloğu veya ön metin eklerse ilk { ... son } aralığını al.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            logger.LogWarning("AI response does not contain valid JSON bounds. Raw: {Raw}", raw);
            throw new ForecastException(Lang.T("Yapay zeka cevabı JSON içermiyor.", "The AI response does not contain JSON."));
        }

        try
        {
            return JsonSerializer.Deserialize<AiStatsResponse>(raw[start..(end + 1)], JsonOpts)
                   ?? throw new JsonException("empty");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "AI JSON parse failed. Raw: {Raw}", raw);
            throw new ForecastException(Lang.T("Yapay zeka cevabı okunamadı, tekrar deneyin.", "The AI response could not be read, please try again."));
        }
    }
}