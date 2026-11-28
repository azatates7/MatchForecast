using System.Globalization;
using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;
using MatchForecast.Api.Services.Redis;

namespace MatchForecast.Api.Services.ApiFootball;

// API-Football (api-sports.io v3) üzerinden fikstürleri çeker; kota için sonuçlar Redis'te önbelleğe alınır. Oranlar artık kullanılmaz.
public sealed class ApiFootballOddsProvider(
    HttpClient http,
    IRedisCacheService cache,
    IOptions<OddsProviderOptions> options,
    ILogger<ApiFootballOddsProvider> logger) : IOddsProvider
{
    private readonly OddsProviderOptions _opt = options.Value;
    private TimeSpan CacheTtl => TimeSpan.FromMinutes(_opt.CacheMinutes);
    private static readonly TimeSpan LiveCacheTtl = TimeSpan.FromMinutes(1);

    private static readonly HashSet<string> PopularLeagues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Süper Lig", "Super Lig",
        "UEFA Champions League", "Champions League",
        "Premier League",
        "La Liga",
        "Serie A",
        "Bundesliga",
        "Ligue 1",
        "UEFA Europa League", "Europa League",
        "UEFA Europa Conference League", "Conference League"
    };

    public async Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct)
    {
        var key = $"fixtures:{date:yyyy-MM-dd}";
        var cached = await cache.GetAsync<List<MatchSummary>>(key);
        if (cached is not null)
            return cached;

        var tz = Uri.EscapeDataString(_opt.Timezone);
        using var doc = await GetAsync($"fixtures?date={date:yyyy-MM-dd}&timezone={tz}", ct);
        var list = doc.RootElement.GetProperty("response")
            .EnumerateArray()
            .Select(ParseFixture)
            .Where(m => !MatchStatus.IsFinished(m.Status))
            .Where(m => !MatchStatus.IsStaleNotStarted(m.Status, m.Kickoff, DateTimeOffset.Now))
            .OrderBy(m => m.Kickoff)
            .ToList();

        // Canlı maç varsa dakika bilgisi eskimesin diye kısa önbellek; yoksa normal süre.
        await cache.SetAsync(key, list, list.Any(m => MatchStatus.IsLive(m.Status)) ? LiveCacheTtl : CacheTtl);
        return list;
    }

    public async Task<IReadOnlyList<MatchSummary>> GetPopularMatchesAsync(DateOnly date, int count, CancellationToken ct)
    {
        var all = await GetMatchesAsync(date, ct);
        return all
            .OrderByDescending(m => PopularLeagues.Contains(m.League))
            .ThenBy(m => m.Kickoff)
            .Take(count)
            .ToList();
    }

    public async Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct)
    {
        var key = $"fixture:{fixtureId}";
        var cached = await cache.GetAsync<MatchSummary>(key);
        if (cached is not null)
            return cached;

        var tz = Uri.EscapeDataString(_opt.Timezone);
        using var doc = await GetAsync($"fixtures?id={fixtureId}&timezone={tz}", ct);
        var items = doc.RootElement.GetProperty("response");
        if (items.GetArrayLength() == 0)
            return null;

        var match = ParseFixture(items[0]);
        await cache.SetAsync(key, match, CacheTtl);
        return match;
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var res = await http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogError("API-Football HTTP request to {Path} failed with status code {StatusCode}", path, (int)res.StatusCode);
            throw new ForecastException(Lang.T($"API-Football HTTP {(int)res.StatusCode} döndü.", $"API-Football returned HTTP {(int)res.StatusCode}."));
        }

        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        // API-Football hataları 200 ile "errors" alanında döner (hatalı anahtar, kota, plan kısıtı vb.)
        if (doc.RootElement.TryGetProperty("errors", out var errors) && HasItems(errors))
        {
            var msg = errors.GetRawText();
            var known = KnownError(errors);
            doc.Dispose();
            logger.LogWarning("API-Football error for {Path}: {Errors}", path, msg);
            throw known ?? new ForecastException(Lang.T($"API-Football hatası: {msg}", $"API-Football error: {msg}"));
        }
        return doc;
    }

    /*
     * Uygulama arızası olmayan, beklenen API-Football hataları 5xx yerine 4xx döner; böylece hata maili tetiklenmez
     * (ExceptionMiddleware yalnızca 5xx'i mailler) ve kullanıcı anlaşılır bir mesaj görür.
     * - plan: ücretsiz plan bu tarihi vermiyor (ör. yarından sonrası) → 400.
     * - requests: günlük istek kotası doldu → 429 (kota uyarısını ApiFootballQuotaTracker ayrıca, günde bir kez mailler).
     * - rateLimit: dakikalık sınır aşıldı → 429.
     */
    private static ForecastException? KnownError(JsonElement errors)
    {
        if (errors.ValueKind != JsonValueKind.Object)
            return null;
        if (errors.TryGetProperty("plan", out _))
            return new ForecastException(Lang.T(
                "Bu tarihin maçları API-Football planında açık değil; yalnızca yakın tarihler görüntülenebilir.",
                "Matches for this date are not available on the API-Football plan; only nearby dates can be viewed."),
                StatusCodes.Status400BadRequest);
        if (errors.TryGetProperty("requests", out _))
            return new ForecastException(Lang.T(
                "API-Football günlük istek kotası doldu; maç listesi kota yenilenince tekrar alınabilir.",
                "The API-Football daily request quota is exhausted; matches can be loaded again once it resets."),
                StatusCodes.Status429TooManyRequests);
        if (errors.TryGetProperty("rateLimit", out _))
            return new ForecastException(Lang.T(
                "API-Football'a kısa sürede çok fazla istek gönderildi; biraz sonra tekrar deneyin.",
                "Too many requests were sent to API-Football in a short time; please try again shortly."),
                StatusCodes.Status429TooManyRequests);
        return null;
    }

    private static bool HasItems(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.GetArrayLength() > 0,
        JsonValueKind.Object => e.EnumerateObject().Any(),
        _ => false
    };

    private static MatchSummary ParseFixture(JsonElement item)
    {
        var fixture = item.GetProperty("fixture");
        var league = item.GetProperty("league");
        var teams = item.GetProperty("teams");
        var status = fixture.GetProperty("status");
        return new MatchSummary(
            fixture.GetProperty("id").GetInt32(),
            DateTimeOffset.Parse(fixture.GetProperty("date").GetString()!, CultureInfo.InvariantCulture),
            league.GetProperty("name").GetString() ?? "",
            league.GetProperty("country").GetString() ?? "",
            teams.GetProperty("home").GetProperty("name").GetString() ?? "",
            teams.GetProperty("away").GetProperty("name").GetString() ?? "",
            status.GetProperty("short").GetString() ?? "",
            OptionalInt(status, "elapsed"),
            OptionalInt(status, "extra"),
            ParseReferee(fixture));
    }

    // "Halil Umut Meler, Turkey" → "Halil Umut Meler". Atama açıklanmadıysa alan null veya boş gelir.
    public static string? ParseReferee(JsonElement fixture)
    {
        if (!fixture.TryGetProperty("referee", out var r) || r.ValueKind != JsonValueKind.String)
            return null;
        var name = r.GetString()!.Split(',', 2)[0].Trim();
        return name.Length == 0 ? null : name;
    }

    private static int? OptionalInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}