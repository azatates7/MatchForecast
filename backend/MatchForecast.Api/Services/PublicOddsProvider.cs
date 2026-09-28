using System.Globalization;
using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MatchForecast.Api.Services;

/// <summary>
/// Simple public odds/provider that fetches today's football matches from ESPN's public scoreboard API.
/// No API key is required. The data is limited and may not contain odds, but it provides match information
/// sufficient for the existing Forecast pipeline (which will still call the Claude AI for forecasts).
/// </summary>
public sealed class PublicOddsProvider(HttpClient http, IMemoryCache cache, ILogger<PublicOddsProvider> logger) : IOddsProvider
{
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(10);

    // ESPN public scoreboard endpoint (no auth required)
    // Example: https://site.api.espn.com/apis/v2/sports/football/scoreboard?dates=2024-09-26
    private string BuildUrl(DateOnly date) => $"apis/v2/sports/football/scoreboard?dates={date:yyyy-MM-dd}";

    public async Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct)
    {
        var cacheKey = $"public:matches:{date:yyyy-MM-dd}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
            var url = BuildUrl(date);
            using var doc = await GetAsync(url, ct);
            var matches = ParseScoreboard(doc);
            return (IReadOnlyList<MatchSummary>)matches;
        }) ?? [];
    }

    public async Task<IReadOnlyList<MatchSummary>> GetPopularMatchesAsync(DateOnly date, int count, CancellationToken ct)
    {
        var all = await GetMatchesAsync(date, ct);
        // ESPN does not expose league popularity, so we simply take the earliest kickoff times.
        return all
            .OrderBy(m => m.Kickoff)
            .Take(count)
            .ToList();
    }

    public Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct)
    {
        // Not required for current workflows; implement a simple lookup in cached list.
        var cacheKey = $"public:matches:all"; // placeholder – we will load today's matches and find id.
        // For simplicity, we fetch today’s matches again.
        var today = DateOnly.FromDateTime(DateTime.Today);
        return GetMatchesAsync(today, ct).ContinueWith(t => t.Result.FirstOrDefault(m => m.Id == fixtureId), ct);
    }

    public Task<MatchOdds?> GetOddsAsync(int fixtureId, CancellationToken ct)
    {
        // Public ESPN endpoint does not provide odds. Return null to indicate unavailable.
        return Task.FromResult<MatchOdds?>(null);
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var res = await http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogError("PublicOddsProvider request to {Path} failed with status {StatusCode}", path, (int)res.StatusCode);
            throw new ForecastException($"PublicOddsProvider HTTP {(int)res.StatusCode} returned.");
        }
        return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private static List<MatchSummary> ParseScoreboard(JsonDocument doc)
    {
        var list = new List<MatchSummary>();
        if (!doc.RootElement.TryGetProperty("events", out var events))
            return list;
        foreach (var ev in events.EnumerateArray())
        {
            try
            {
                var id = ev.GetProperty("id").GetInt32();
                var status = ev.GetProperty("status").GetProperty("type").GetProperty("name").GetString() ?? "";
                var dateStr = ev.GetProperty("date").GetString() ?? "";
                var kickoff = DateTimeOffset.Parse(dateStr, CultureInfo.InvariantCulture);

                var competitions = ev.GetProperty("competitions").EnumerateArray();
                var comp = competitions.First();
                var league = comp.GetProperty("type").GetProperty("name").GetString() ?? "";
                var country = comp.GetProperty("venue").GetProperty("country").GetString() ?? "";

                var competitors = comp.GetProperty("competitors").EnumerateArray().ToArray();
                var home = competitors.First(c => c.GetProperty("homeAway").GetString() == "home");
                var away = competitors.First(c => c.GetProperty("homeAway").GetString() == "away");
                var homeTeam = home.GetProperty("team").GetProperty("name").GetString() ?? "";
                var awayTeam = away.GetProperty("team").GetProperty("name").GetString() ?? "";

                list.Add(new MatchSummary(id, kickoff, league, country, homeTeam, awayTeam, status));
            }
            catch (Exception ex)
            {
                // Swallow parsing errors for individual events; continue with others.
                // Log at debug level.
                // (logger instance not available here; ignore to keep parsing lightweight.)
            }
        }
        return list;
    }
}
