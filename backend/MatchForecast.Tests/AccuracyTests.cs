using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Accuracy;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

// İsabet takibi: seçeneklerin skora göre değerlendirilmesi, rapor hesabı, sonuç toplama ve yetkili rapor endpoint'i.
public class AccuracyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTime Kickoff = new(2026, 11, 20, 20, 0, 0);

    // ---------- PredictionSettler ----------

    [Theory]
    [InlineData("Match Winner", "Home", 2, 1, true)]
    [InlineData("Match Winner", "Draw", 2, 1, false)]
    [InlineData("Double Chance", "Draw/Away", 1, 1, true)]
    [InlineData("Double Chance", "Home/Away", 1, 1, false)]
    [InlineData("Goals Over/Under", "Over 2.5", 2, 1, true)]
    [InlineData("Goals Over/Under", "Under 2.5", 2, 1, false)]
    [InlineData("Both Teams Score", "Yes", 2, 0, false)]
    [InlineData("Both Teams Score", "No", 2, 0, true)]
    [InlineData("Home Team Goals", "Over 1.5", 2, 0, true)]
    [InlineData("Away Team Goals", "Under 0.5", 2, 0, true)]
    [InlineData("First Half Winner", "Away", 2, 1, true)]    // ilk yarı 0-1
    [InlineData("Goals Over/Under First Half", "Over 0.5", 2, 1, true)]
    public void Settle_UsesFullTimeAndHalfTimeScores(string market, string selection, int home, int away, bool expected)
    {
        var result = Result(1, home, away, htHome: 0, htAway: 1);

        Assert.Equal(expected, PredictionSettler.Settle(market, selection, result));
    }

    [Theory]
    [InlineData("Yellow Cards Over/Under", "Over 3.5")] // sarı kart istatistiği yok
    [InlineData("First Half Winner", "Home")]           // ilk yarı skoru yok
    [InlineData("Unknown", "Home")]
    public void Settle_ReturnsNull_WhenItCannotBeDecided(string market, string selection)
    {
        Assert.Null(PredictionSettler.Settle(market, selection, Result(1, 1, 0)));
    }

    [Theory]
    [InlineData("Over 3.5", true)]   // 2 + 3 = 5 sarı
    [InlineData("Under 4.5", false)]
    public void Settle_YellowCards_UsesBothTeamsCards(string selection, bool expected)
    {
        var result = Result(1, 1, 0);
        result.HomeYellowCards = 2;
        result.AwayYellowCards = 3;

        Assert.Equal(expected, PredictionSettler.Settle("Yellow Cards Over/Under", selection, result));
    }

    // ---------- AccuracyReportBuilder ----------

    [Fact]
    public void Build_ComputesResultAccuracy_Brier_Calibration_AndAbsenceGroups()
    {
        var forecasts = new List<AccuracyForecast>
        {
            // 1) Ev %60 → 2-1 tuttu; 2 seçenekten 1'i tuttu; eksik listesiyle üretildi.
            Forecast(1, (60, 25, 15), absences: "Ready",
                ("Match Winner", "Home", 60), ("Goals Over/Under", "Under 2.5", 72), ("Yellow Cards Over/Under", "Over 3.5", 66)),
            // 2) Deplasman %50 → 1-1 tutmadı; KG Var tuttu; eksik listesi yoktu.
            Forecast(2, (20, 30, 50), absences: "Unavailable", ("Both Teams Score", "Yes", 64)),
            Forecast(3, (40, 30, 30), absences: null),                 // sonuç yok → bekliyor
            Forecast(4, (40, 30, 30), absences: null),                 // ertelendi → void
            Forecast(5, (40, 30, 30), absences: null, generatedAfterKickoff: true) // maç başladıktan sonra üretildi
        };
        var results = new Dictionary<int, MatchResult>
        {
            [1] = Result(1, 2, 1, 1, 0),
            [2] = Result(2, 1, 1, 0, 0),
            [4] = new() { FixtureId = 4, Status = "PST" },
            [5] = Result(5, 0, 0, 0, 0)
        };

        var report = AccuracyReportBuilder.Build(forecasts, results, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        var t = report.Totals;
        Assert.Equal((5, 2, 1, 1, 1), (t.Forecasts, t.Evaluated, t.Pending, t.Void, t.Excluded));
        Assert.Equal((1, 50.0), (t.ResultHits, t.ResultAccuracy));
        // Brier: (0.4² + 0.25² + 0.15²) = 0.245 ve (0.2² + 0.7² + 0.5²) = 0.78 → ortalama 0.5125
        Assert.InRange(t.BrierScore!.Value, 0.512, 0.513);
        Assert.Equal((3, 2, 66.7, 1), (t.Picks, t.PickHits, t.PickHitRate, t.UnsettledPicks));

        // 60 ve 64 aynı aralıkta (biri tuttu), 72 tutmadı (2-1 = 3 gol).
        var b60 = Assert.Single(report.Calibration, b => b.From == 60);
        Assert.Equal((2, 2, 100.0), (b60.Picks, b60.Hits, b60.HitRate));
        var b70 = Assert.Single(report.Calibration, b => b.From == 70);
        Assert.Equal((1, 0, 0.0), (b70.Picks, b70.Hits, b70.HitRate));

        var with = Assert.Single(report.ByAbsences, g => g.Key == "WithAbsences");
        Assert.Equal((1, 1, 100.0), (with.Matches, with.ResultHits, with.ResultAccuracy));
        var without = Assert.Single(report.ByAbsences, g => g.Key == "WithoutAbsences");
        Assert.Equal((1, 0, 0.0), (without.Matches, without.ResultHits, without.ResultAccuracy));

        Assert.Equal(new[] { 2, 1 }, report.Recent.Select(r => r.FixtureId)); // en yeni önce
        Assert.Equal(("2-1", "1-0", "Home", true), (report.Recent[1].Score, report.Recent[1].HalfTimeScore, report.Recent[1].PredictedResult, report.Recent[1].ResultHit));
        Assert.Equal("Maç sonucu", Assert.Single(report.Markets, m => m.Market == "Match Winner").Label.Tr);
    }

    // ---------- ResultSettlementWorker ----------

    [Fact]
    public async Task SettleAsync_SavesFinishedAndVoidMatches_SkipsLiveAndOld()
    {
        var now = new DateTime(2026, 11, 27, 12, 0, 0);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        db.Forecasts.AddRange(
            Record(1, now.AddHours(-20)),  // bitti
            Record(2, now.AddHours(-18)),  // ertelendi
            Record(3, now.AddHours(-3)),   // hâlâ canlı görünüyor
            Record(4, now.AddHours(-1)),   // 150 dk dolmadı → sorulmaz
            Record(5, now.AddDays(-10)),   // LookbackDays dışında → vazgeçildi
            Record(6, now.AddHours(-30)));  // sonucu zaten var
        db.MatchResults.Add(new MatchResult { FixtureId = 6, Status = "FT", HomeGoals = 0, AwayGoals = 0 });
        await db.SaveChangesAsync();

        var provider = new FakeResultProvider(
            new FixtureResult(1, "FT", 2, 1, 1, 1),
            new FixtureResult(2, "PST", null, null, null, null),
            new FixtureResult(3, "2H", 1, 0, 1, 0));

        var (requested, saved) = await ResultSettlementWorker.SettleAsync(db, provider, new AccuracyOptions(), now, CancellationToken.None);

        Assert.Equal((3, 2), (requested, saved));
        Assert.Equal(new[] { 1, 2, 3 }, provider.Requested.Order());
        var finished = await db.MatchResults.SingleAsync(r => r.FixtureId == 1);
        Assert.Equal(("FT", 2, 1, 1, 1), (finished.Status, finished.HomeGoals, finished.AwayGoals, finished.HalfTimeHomeGoals, finished.HalfTimeAwayGoals));
        var postponed = await db.MatchResults.SingleAsync(r => r.FixtureId == 2);
        Assert.Null(postponed.HomeGoals);
        Assert.False(await db.MatchResults.AnyAsync(r => r.FixtureId == 3));
    }

    [Fact]
    public void ResultProvider_ParsesRegularTimeScore_NotExtraTime()
    {
        using var doc = JsonDocument.Parse("""
            { "fixture": { "id": 77, "status": { "short": "AET" } },
              "goals": { "home": 2, "away": 1 },
              "score": { "halftime": { "home": 0, "away": 1 }, "fulltime": { "home": 1, "away": 1 }, "extratime": { "home": 1, "away": 0 } } }
            """);

        var r = ApiFootballResultProvider.Parse(doc.RootElement);

        Assert.Equal(new FixtureResult(77, "AET", 1, 1, 0, 1), r);
    }

    [Fact]
    public void ResultProvider_ReadsYellowCardsFromStatistics_ForRegularTimeMatches()
    {
        using var doc = JsonDocument.Parse("""
            { "fixture": { "id": 78, "status": { "short": "FT" } },
              "teams": { "home": { "id": 10 }, "away": { "id": 20 } },
              "score": { "halftime": { "home": 1, "away": 0 }, "fulltime": { "home": 2, "away": 1 } },
              "statistics": [
                { "team": { "id": 20 }, "statistics": [ { "type": "Fouls", "value": 14 }, { "type": "Yellow Cards", "value": 3 } ] },
                { "team": { "id": 10 }, "statistics": [ { "type": "Yellow Cards", "value": null } ] } ] }
            """);

        var r = ApiFootballResultProvider.Parse(doc.RootElement);

        Assert.Equal((0, 3), (r.HomeYellowCards, r.AwayYellowCards)); // null değer = kart yok
    }

    [Fact]
    public void ResultProvider_LeavesYellowCardsNull_WithoutStatistics()
    {
        using var doc = JsonDocument.Parse("""
            { "fixture": { "id": 79, "status": { "short": "FT" } }, "teams": { "home": { "id": 1 }, "away": { "id": 2 } },
              "score": { "fulltime": { "home": 0, "away": 0 } }, "statistics": [] }
            """);

        var r = ApiFootballResultProvider.Parse(doc.RootElement);

        Assert.Null(r.HomeYellowCards);
        Assert.Null(r.AwayYellowCards);
    }

    // ---------- Endpoint ----------

    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.Guest)]
    public async Task Report_IsForbidden_WithoutPermission(string role)
    {
        var client = factory.WithTestJwt().CreateClientFor(2, [role]);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accuracy")).StatusCode);
    }

    [Fact]
    public async Task Report_ReturnsEvaluatedMatches_ForPermittedUser()
    {
        var testFactory = factory.WithTestJwt();
        var kickoff = DateTime.Now.AddDays(-1);
        using (var scope = testFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var f = Forecast(42, (55, 25, 20), "Ready", ("Match Winner", "Home", 55));
            db.Forecasts.Add(new ForecastRecord
            {
                FixtureId = 42, Kickoff = kickoff, League = "Süper Lig", HomeTeam = "A", AwayTeam = "B",
                GeneratedAt = kickoff.AddHours(-2), ResultJson = f.ResultJson, CreatedAt = kickoff
            });
            db.MatchResults.Add(Result(42, 3, 0, 1, 0));
            await db.SaveChangesAsync();
        }

        var client = testFactory.CreateClientFor(3, [RoleNames.User], PermissionKeys.AccuracyView);
        var report = (await client.GetFromJsonAsync<AccuracyReportResponse>("/api/accuracy?days=7"))!;

        Assert.Equal((1, 1, 100.0), (report.Totals.Evaluated, report.Totals.ResultHits, report.Totals.ResultAccuracy));
        Assert.Equal("3-0", Assert.Single(report.Recent).Score);
    }

    // ---------- yardımcılar ----------

    private static MatchResult Result(int id, int home, int away, int? htHome = null, int? htAway = null) => new()
    {
        FixtureId = id, Status = "FT", HomeGoals = home, AwayGoals = away, HalfTimeHomeGoals = htHome, HalfTimeAwayGoals = htAway
    };

    // ForecastStore'un yazdığı biçim (camelCase JSON); rapor yalnızca outcomes / predictions / absences.state okur.
    private static AccuracyForecast Forecast(int id, (int Home, int Draw, int Away) outcomes, string? absences,
        params (string Market, string Selection, int Confidence)[] picks) =>
        Forecast(id, outcomes, absences, false, picks);

    private static AccuracyForecast Forecast(int id, (int Home, int Draw, int Away) outcomes, string? absences, bool generatedAfterKickoff,
        params (string Market, string Selection, int Confidence)[] picks)
    {
        var json = JsonSerializer.Serialize(new
        {
            outcomes = new { home = outcomes.Home, draw = outcomes.Draw, away = outcomes.Away, bttsYes = 50 },
            predictions = picks.Select((p, i) => new { rank = i + 1, market = p.Market, selection = p.Selection, confidence = p.Confidence }),
            absences = absences is null ? null : new { state = absences }
        });
        var kickoff = Kickoff.AddHours(id);
        return new AccuracyForecast(id, kickoff, "Süper Lig", $"Ev{id}", $"Dep{id}",
            generatedAfterKickoff ? kickoff.AddMinutes(30) : kickoff.AddHours(-3), json);
    }

    private static ForecastRecord Record(int fixtureId, DateTime kickoff) => new()
    {
        FixtureId = fixtureId, Kickoff = kickoff, GeneratedAt = kickoff.AddHours(-2), ResultJson = "{}", CreatedAt = kickoff
    };

    private sealed class FakeResultProvider(params FixtureResult[] results) : IMatchResultProvider
    {
        public List<int> Requested { get; } = [];

        public Task<IReadOnlyList<FixtureResult>> GetResultsAsync(IReadOnlyCollection<int> fixtureIds, CancellationToken ct)
        {
            Requested.AddRange(fixtureIds);
            return Task.FromResult<IReadOnlyList<FixtureResult>>(results.Where(r => fixtureIds.Contains(r.FixtureId)).ToList());
        }
    }
}