using System.Globalization;
using System.Text.Json;
using MatchForecast.Api.Models;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services;

/// <summary>
/// API-Football (api-sports.io v3) üzerinden fikstür ve tüm bahis marketlerini çeker.
/// DB yok; kota tüketimini azaltmak için sonuçlar bellekte önbelleğe alınır.
/// </summary>
public sealed class ApiFootballOddsProvider(
    HttpClient http,
    IMemoryCache cache,
    IOptions<OddsProviderOptions> options,
    ILogger<ApiFootballOddsProvider> logger) : IOddsProvider
{
    private readonly OddsProviderOptions _opt = options.Value;
    private TimeSpan CacheTtl => TimeSpan.FromMinutes(_opt.CacheMinutes);

    public async Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct)
    {
        var key = $"fixtures:{date:yyyy-MM-dd}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var tz = Uri.EscapeDataString(_opt.Timezone);
            using var doc = await GetAsync($"fixtures?date={date:yyyy-MM-dd}&timezone={tz}", ct);
            return (IReadOnlyList<MatchSummary>)doc.RootElement.GetProperty("response")
                .EnumerateArray()
                .Select(ParseFixture)
                .OrderBy(m => m.Kickoff)
                .ToList();
        }) ?? [];
    }

    public async Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync($"fixture:{fixtureId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var tz = Uri.EscapeDataString(_opt.Timezone);
            using var doc = await GetAsync($"fixtures?id={fixtureId}&timezone={tz}", ct);
            var items = doc.RootElement.GetProperty("response");
            return items.GetArrayLength() == 0 ? null : ParseFixture(items[0]);
        });
    }

    public async Task<MatchOdds?> GetOddsAsync(int fixtureId, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync($"odds:{fixtureId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            using var doc = await GetAsync($"odds?fixture={fixtureId}", ct);
            var response = doc.RootElement.GetProperty("response");
            if (response.GetArrayLength() == 0) return null;

            var bookmakers = response[0].GetProperty("bookmakers").EnumerateArray().ToList();
            if (bookmakers.Count == 0) return null;

            // Yapılandırılmış bahisçi varsa onu, yoksa en çok market sunanı al.
            var chosen = _opt.BookmakerId is int id
                ? bookmakers.FirstOrDefault(b => b.GetProperty("id").GetInt32() == id)
                : default;
            if (chosen.ValueKind == JsonValueKind.Undefined)
                chosen = bookmakers.MaxBy(b => b.GetProperty("bets").GetArrayLength());

            var markets = chosen.GetProperty("bets").EnumerateArray()
                .Select(bet => new Market(
                    bet.GetProperty("id").GetInt32(),
                    bet.GetProperty("name").GetString() ?? "",
                    bet.GetProperty("values").EnumerateArray()
                        .Select(v => new OddOption(AsText(v.GetProperty("value")), ParseOdd(v.GetProperty("odd"))))
                        .Where(o => o.Odd > 1m)
                        .ToList()))
                .Where(m => m.Options.Count > 0)
                .ToList();

            return new MatchOdds(fixtureId, chosen.GetProperty("name").GetString() ?? "", markets);
        });
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var res = await http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode)
            throw new ForecastException($"API-Football HTTP {(int)res.StatusCode} döndü.");

        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        // API-Football hataları 200 ile "errors" alanında döner (hatalı anahtar, kota, plan kısıtı vb.)
        if (doc.RootElement.TryGetProperty("errors", out var errors) && HasItems(errors))
        {
            var msg = errors.GetRawText();
            doc.Dispose();
            logger.LogWarning("API-Football error for {Path}: {Errors}", path, msg);
            throw new ForecastException($"API-Football hatası: {msg}");
        }
        return doc;
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
        return new MatchSummary(
            fixture.GetProperty("id").GetInt32(),
            DateTimeOffset.Parse(fixture.GetProperty("date").GetString()!, CultureInfo.InvariantCulture),
            league.GetProperty("name").GetString() ?? "",
            league.GetProperty("country").GetString() ?? "",
            teams.GetProperty("home").GetProperty("name").GetString() ?? "",
            teams.GetProperty("away").GetProperty("name").GetString() ?? "",
            fixture.GetProperty("status").GetProperty("short").GetString() ?? "");
    }

    private static string AsText(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText();

    private static decimal ParseOdd(JsonElement e) =>
        decimal.TryParse(AsText(e), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
