using System.Text.Json;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Accuracy;

/// <param name="HomeGoals">Normal süre (90 dk) skoru; maç oynanmadıysa (ertelendi, iptal) null.</param>
/// <param name="HomeYellowCards">Maç istatistiğindeki sarı kart; istatistik yoksa veya maç uzatmaya gittiyse null.</param>
public sealed record FixtureResult(int FixtureId, string Status, int? HomeGoals, int? AwayGoals, int? HalfTimeHomeGoals, int? HalfTimeAwayGoals,
    int? HomeYellowCards = null, int? AwayYellowCards = null);

public interface IMatchResultProvider
{
    /// <summary>En fazla 20 maçın güncel durumunu ve skorunu tek istekte döndürür. Sağlayıcıda bulunamayan maç listede olmaz.</summary>
    Task<IReadOnlyList<FixtureResult>> GetResultsAsync(IReadOnlyCollection<int> fixtureIds, CancellationToken ct);
}

/*
 * API-Football fixtures?ids=1-2-3 (en fazla 20 id). Fikstür listesindeki (IOddsProvider) sağlayıcıdan ayrıdır:
 * o arayüzün sahte/ESPN uygulamaları da var, sonuç toplama yalnızca API-Football ile yapılır.
 * Skor kaynağı score.fulltime / score.halftime: uzatmalı (AET) ve penaltılı (PEN) maçlarda da 90 dakikanın skorudur;
 * "goals" alanı uzatma gollerini de içerdiği için kullanılmaz.
 * Sarı kartlar: ids ile yapılan sorguda her maçın "statistics" dizisi de gelir (ek istek yok). İstatistik tüm maçı kapsar; uzatmalı maçlarda 90 dakikaya ayrılamadığı için kullanılmaz (null).
 */
public sealed class ApiFootballResultProvider(HttpClient http, IOptions<OddsProviderOptions> options, ILogger<ApiFootballResultProvider> logger)
    : IMatchResultProvider
{
    public const int MaxIdsPerRequest = 20;

    public async Task<IReadOnlyList<FixtureResult>> GetResultsAsync(IReadOnlyCollection<int> fixtureIds, CancellationToken ct)
    {
        if (fixtureIds.Count == 0)
            return [];
        if (fixtureIds.Count > MaxIdsPerRequest)
            throw new ArgumentException($"API-Football accepts at most {MaxIdsPerRequest} ids per request.", nameof(fixtureIds));

        var tz = Uri.EscapeDataString(options.Value.Timezone);
        using var res = await http.GetAsync($"fixtures?ids={string.Join('-', fixtureIds)}&timezone={tz}", ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"API-Football returned HTTP {(int)res.StatusCode} for results.", null, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        // Hatalar (kota, plan kısıtı) 200 ile "errors" alanında döner.
        if (doc.RootElement.TryGetProperty("errors", out var errors)
            && (errors.ValueKind == JsonValueKind.Object ? errors.EnumerateObject().Any() : errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0))
        {
            logger.LogWarning("API-Football results error: {Errors}", errors.GetRawText());
            throw new InvalidOperationException($"API-Football error: {errors.GetRawText()}");
        }

        return doc.RootElement.GetProperty("response").EnumerateArray().Select(Parse).ToList();
    }

    public static FixtureResult Parse(JsonElement item)
    {
        var fixture = item.GetProperty("fixture");
        var score = item.TryGetProperty("score", out var s) ? s : default;
        var status = fixture.GetProperty("status").GetProperty("short").GetString() ?? "";
        var regularTime = status is "FT";
        return new FixtureResult(
            fixture.GetProperty("id").GetInt32(),
            status,
            Goal(score, "fulltime", "home"),
            Goal(score, "fulltime", "away"),
            Goal(score, "halftime", "home"),
            Goal(score, "halftime", "away"),
            regularTime ? YellowCards(item, "home") : null,
            regularTime ? YellowCards(item, "away") : null);
    }

    // statistics[] içinden takımın (teams.home/away.id) "Yellow Cards" değeri. Kart görmeyen takımda değer null gelir → 0.
    // İstatistik hiç yoksa (küçük ligler) null.
    private static int? YellowCards(JsonElement item, string side)
    {
        if (!item.TryGetProperty("statistics", out var stats) || stats.ValueKind != JsonValueKind.Array || stats.GetArrayLength() == 0)
            return null;
        if (!item.TryGetProperty("teams", out var teams) || !teams.TryGetProperty(side, out var team)
            || !team.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return null;
        var teamId = idEl.GetInt32();

        foreach (var entry in stats.EnumerateArray())
        {
            if (!entry.TryGetProperty("team", out var t) || !t.TryGetProperty("id", out var tid)
                || tid.ValueKind != JsonValueKind.Number || tid.GetInt32() != teamId)
                continue;
            if (!entry.TryGetProperty("statistics", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var stat in list.EnumerateArray())
            {
                if (stat.TryGetProperty("type", out var type) && type.GetString() == "Yellow Cards")
                {
                    if (!stat.TryGetProperty("value", out var v)) return null;
                    return v.ValueKind switch
                    {
                        JsonValueKind.Number => v.GetInt32(),
                        JsonValueKind.Null => 0,
                        JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
                        _ => null
                    };
                }
            }
            return null; // takımın istatistiği var ama sarı kart satırı yok
        }
        return null;
    }

    private static int? Goal(JsonElement score, string period, string side) =>
        score.ValueKind == JsonValueKind.Object
        && score.TryGetProperty(period, out var p) && p.ValueKind == JsonValueKind.Object
        && p.TryGetProperty(side, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;
}