using System.Globalization;
using System.Text.Json;
using MatchForecast.Data.Entity;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.Accuracy;

/// <summary>Rapor girdisi: forecasts tablosundaki satırın gereken alanları.</summary>
public sealed record AccuracyForecast(int FixtureId, DateTime Kickoff, string League, string HomeTeam, string AwayTeam, DateTime GeneratedAt, string ResultJson);

/*
 * Seçilen tahmin (Prediction.Market + Selection) gerçekleşti mi? ForecastCatalog'daki market adlarıyla eşleşir. null: ilk yarı skoru veya sarı kart istatistiği yok ya da bilinmeyen market.
 */
public static class PredictionSettler
{
    public static bool? Settle(string market, string selection, MatchResult r)
    {
        if (r.HomeGoals is not { } h || r.AwayGoals is not { } a)
            return null;
        int? hh = r.HalfTimeHomeGoals, ha = r.HalfTimeAwayGoals;

        return market switch
        {
            "Match Winner" => Winner(selection, h, a),
            "Double Chance" => selection switch
            {
                "Home/Draw" => h >= a,
                "Home/Away" => h != a,
                "Draw/Away" => h <= a,
                _ => null
            },
            "Goals Over/Under" => OverUnder(selection, h + a),
            "Both Teams Score" => selection switch { "Yes" => h > 0 && a > 0, "No" => h == 0 || a == 0, _ => null },
            "First Half Winner" => hh is null || ha is null ? null : Winner(selection, hh.Value, ha.Value),
            "Goals Over/Under First Half" => hh is null || ha is null ? null : OverUnder(selection, hh.Value + ha.Value),
            "Home Team Goals" => OverUnder(selection, h),
            "Away Team Goals" => OverUnder(selection, a),
            "Yellow Cards Over/Under" => r.HomeYellowCards is { } hy && r.AwayYellowCards is { } ay ? OverUnder(selection, hy + ay) : null,
            _ => null
        };
    }

    private static bool? Winner(string selection, int home, int away) => selection switch
    {
        "Home" => home > away,
        "Draw" => home == away,
        "Away" => home < away,
        _ => null
    };

    // "Over 2.5" / "Under 2.5"; çizgiler hep buçuklu olduğu için eşitlik (iade) durumu yoktur.
    private static bool? OverUnder(string selection, int value)
    {
        var parts = selection.Split(' ', 2);
        if (parts.Length != 2 || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var line))
            return null;
        return parts[0] switch { "Over" => value > line, "Under" => value < line, _ => null };
    }
}

/*
 * forecasts + match_results satırlarından isabet raporunu hesaplar (saf fonksiyon, veritabanına erişmez).
 * Tahmin JSON'unun yalnızca gereken alanları okunur; eski kayıtlar ve alan eklemeleri raporu bozmaz.
 * Maç başladıktan sonra üretilmiş tahminler (canlıyken yenilenmiş) sonucu bilerek üretilmiş sayılabileceği için dışarıda kalır.
 */
public static class AccuracyReportBuilder
{
    private const int RecentCount = 30;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> VoidStatuses = new(StringComparer.OrdinalIgnoreCase) { "PST", "CANC", "ABD", "AWD", "WO" };
    private static readonly (int From, int To)[] Buckets = [(0, 49), (50, 59), (60, 69), (70, 79), (80, 89), (90, 100)];

    private static readonly Dictionary<string, LocalizedText> MarketLabels = new()
    {
        ["Match Winner"] = new("Maç sonucu", "Match result"),
        ["Double Chance"] = new("Çifte şans", "Double chance"),
        ["Goals Over/Under"] = new("Toplam gol alt/üst", "Total goals over/under"),
        ["Both Teams Score"] = new("Karşılıklı gol", "Both teams to score"),
        ["First Half Winner"] = new("İlk yarı sonucu", "First-half result"),
        ["Goals Over/Under First Half"] = new("İlk yarı gol alt/üst", "First-half goals over/under"),
        ["Home Team Goals"] = new("Ev sahibi gol", "Home team goals"),
        ["Away Team Goals"] = new("Deplasman gol", "Away team goals"),
        ["Yellow Cards Over/Under"] = new("Sarı kart alt/üst", "Yellow cards over/under"),
    };

    private sealed record StoredForecast(StoredOutcomes? Outcomes, List<StoredPrediction>? Predictions, StoredAbsences? Absences);
    private sealed record StoredOutcomes(int Home, int Draw, int Away);
    private sealed record StoredPrediction(string? Market, string? Selection, int Confidence);
    private sealed record StoredAbsences(string? State);

    private sealed record Pick(string Market, int Confidence, bool Hit);

    private sealed record Evaluated(
        AccuracyForecast Forecast, MatchResult Result, string Predicted, int PredictedProbability, bool ResultHit,
        double Brier, IReadOnlyList<Pick> Picks, int Unsettled, bool HadAbsences);

    public static AccuracyReportResponse Build(
        IReadOnlyList<AccuracyForecast> forecasts, IReadOnlyDictionary<int, MatchResult> results, DateOnly from, DateOnly to)
    {
        int pending = 0, voided = 0, excluded = 0;
        var evaluated = new List<Evaluated>();

        foreach (var f in forecasts)
        {
            if (!results.TryGetValue(f.FixtureId, out var r))
            {
                pending++;
                continue;
            }
            if (VoidStatuses.Contains(r.Status) || r.HomeGoals is null || r.AwayGoals is null)
            {
                voided++;
                continue;
            }

            var stored = f.GeneratedAt <= f.Kickoff ? Read(f.ResultJson) : null;
            if (stored?.Outcomes is null)
            {
                excluded++;
                continue;
            }

            evaluated.Add(Evaluate(f, r, stored));
        }

        var picks = evaluated.SelectMany(e => e.Picks).ToList();
        var totals = new AccuracyTotalsResponse(
            forecasts.Count,
            evaluated.Count,
            pending,
            voided,
            excluded,
            evaluated.Count(e => e.ResultHit),
            Rate(evaluated.Count(e => e.ResultHit), evaluated.Count),
            evaluated.Count == 0 ? null : Math.Round(evaluated.Average(e => e.Brier), 3),
            picks.Count,
            picks.Count(p => p.Hit),
            Rate(picks.Count(p => p.Hit), picks.Count),
            picks.Count == 0 ? null : Math.Round(picks.Average(p => p.Confidence), 1),
            evaluated.Sum(e => e.Unsettled));

        var calibration = Buckets
            .Select(b => (b, list: picks.Where(p => p.Confidence >= b.From && p.Confidence <= b.To).ToList()))
            .Where(x => x.list.Count > 0)
            .Select(x => new AccuracyBucketResponse(x.b.From, x.b.To, x.list.Count, x.list.Count(p => p.Hit),
                Rate(x.list.Count(p => p.Hit), x.list.Count), Math.Round(x.list.Average(p => p.Confidence), 1)))
            .ToList();

        var markets = picks
            .GroupBy(p => p.Market)
            .Select(g => new AccuracyMarketResponse(g.Key, MarketLabels.GetValueOrDefault(g.Key, new LocalizedText(g.Key, g.Key)),
                g.Count(), g.Count(p => p.Hit), Rate(g.Count(p => p.Hit), g.Count()), Math.Round(g.Average(p => p.Confidence), 1)))
            .OrderByDescending(m => m.Picks)
            .ToList();

        var byAbsences = new[] { ("WithAbsences", true), ("WithoutAbsences", false) }
            .Select(x => (x.Item1, list: evaluated.Where(e => e.HadAbsences == x.Item2).ToList()))
            .Select(x =>
            {
                var groupPicks = x.list.SelectMany(e => e.Picks).ToList();
                return new AccuracyGroupResponse(x.Item1, x.list.Count, x.list.Count(e => e.ResultHit),
                    Rate(x.list.Count(e => e.ResultHit), x.list.Count),
                    groupPicks.Count, groupPicks.Count(p => p.Hit), Rate(groupPicks.Count(p => p.Hit), groupPicks.Count));
            })
            .ToList();

        var recent = evaluated
            .OrderByDescending(e => e.Forecast.Kickoff)
            .Take(RecentCount)
            .Select(e => new AccuracyMatchResponse(
                e.Forecast.FixtureId, e.Forecast.Kickoff, e.Forecast.League, e.Forecast.HomeTeam, e.Forecast.AwayTeam,
                $"{e.Result.HomeGoals}-{e.Result.AwayGoals}",
                e.Result.HalfTimeHomeGoals is { } hh && e.Result.HalfTimeAwayGoals is { } ha ? $"{hh}-{ha}" : null,
                e.Predicted, e.PredictedProbability, e.ResultHit, e.Picks.Count, e.Picks.Count(p => p.Hit), e.HadAbsences))
            .ToList();

        return new AccuracyReportResponse(from, to, totals, calibration, markets, byAbsences, recent);
    }

    private static Evaluated Evaluate(AccuracyForecast f, MatchResult r, StoredForecast stored)
    {
        var o = stored.Outcomes!;
        int h = r.HomeGoals!.Value, a = r.AwayGoals!.Value;
        var actual = h > a ? "Home" : h == a ? "Draw" : "Away";

        // En yüksek olasılık; eşitlikte sıra Ev, Beraberlik, Deplasman.
        var (predicted, probability) = new[] { ("Home", o.Home), ("Draw", o.Draw), ("Away", o.Away) }
            .OrderByDescending(x => x.Item2).First();

        double Sq(int percent, string outcome) => Math.Pow(percent / 100.0 - (actual == outcome ? 1 : 0), 2);
        var brier = Sq(o.Home, "Home") + Sq(o.Draw, "Draw") + Sq(o.Away, "Away");

        var picks = new List<Pick>();
        var unsettled = 0;
        foreach (var p in stored.Predictions ?? [])
        {
            var hit = p.Market is null || p.Selection is null ? null : PredictionSettler.Settle(p.Market, p.Selection, r);
            if (hit is null)
                unsettled++;
            else
                picks.Add(new Pick(p.Market!, p.Confidence, hit.Value));
        }

        // Ön bilgiyle (maç gününden önce) üretilen tahmin de eksik listesini kullanmıştır.
        var hadAbsences = stored.Absences?.State is { } state
                          && (state.Equals(AbsenceReport.Ready, StringComparison.OrdinalIgnoreCase)
                              || state.Equals(AbsenceReport.Preliminary, StringComparison.OrdinalIgnoreCase));
        return new Evaluated(f, r, predicted, probability, predicted == actual, brier, picks, unsettled, hadAbsences);
    }

    private static StoredForecast? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<StoredForecast>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? Rate(int hits, int total) => total == 0 ? null : Math.Round(hits * 100.0 / total, 1);
}