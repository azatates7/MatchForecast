using System.Net;
using System.Text.Json;
using System.Text;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Absences;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MatchForecast.Tests;

// Maç günü kuralı, Google araması isteği, cevabın ayrıştırılması (takım eşleme, durum normalizasyonu, kaynaklar), kullanım kaydı (token ve arama sayısı) ve fail-open.
public class GeminiAbsenceResearcherTests
{
    private static MatchSummary Match(DateTimeOffset kickoff) =>
        new(1636402, kickoff, "UEFA Europa Conference League", "World", "CSKA Sofia", "Trabzonspor", "NS");

    private const string GroundedResponse = """
        {
          "candidates": [{
            "content": { "parts": [ { "text": "```json\n{\"players\":[{\"team\":\"Trabzonspor\",\"player\":\"Okay Yokuşlu\",\"status\":\"Injured\",\"reason\":\"Darbe\",\"reasonEn\":\"Knock\"},{\"team\":\"Trabzonspor A.Ş.\",\"player\":\"Umut Nayir\",\"status\":\"NotInSquad\",\"reason\":\"UEFA listesinde yok\",\"reasonEn\":\"Not in UEFA list\"},{\"team\":\"CSKA Sofia\",\"player\":\"X. Player\",\"status\":\"maybe\",\"reason\":\"Belirsiz\"},{\"team\":\"Başka Takım\",\"player\":\"Y\",\"status\":\"Injured\"},{\"team\":\"Trabzonspor\",\"player\":\"okay yokuşlu\",\"status\":\"Injured\"}]}\n```" } ] },
            "groundingMetadata": { "webSearchQueries": ["Trabzonspor sakat", "CSKA Sofia eksik", "", "trabzonspor sakat"], "groundingChunks": [
              { "web": { "uri": "https://vertexaisearch.example/a", "title": "trabzonspor.org.tr" } },
              { "web": { "uri": "https://vertexaisearch.example/b", "title": "uefa.com" } },
              { "web": { "uri": "https://vertexaisearch.example/c", "title": "trabzonspor.org.tr" } } ] }
          }],
          "usageMetadata": { "promptTokenCount": 400, "toolUsePromptTokenCount": 600, "candidatesTokenCount": 150, "thoughtsTokenCount": 50 }
        }
        """;

    // groundingChunks yok (Gemini bazen göndermiyor); kaynaklar modelin JSON'undan alınır.
    private const string ResponseWithModelSources = """
        {
          "candidates": [{
            "content": { "parts": [ { "text": "{\"players\":[],\"sources\":[{\"title\":\"Kulüp\",\"url\":\"https://club.example/news\"},{\"title\":\"Uydurma\",\"url\":\"https://club.example/missing\"},{\"title\":\"Kulüp\",\"url\":\"https://club.example/news\"}]}" } ] }
          }]
        }
        """;

    private readonly Mock<IAiUsageStore> _usage = new();
    private readonly Mock<ISourceLinkChecker> _links = new();

    public GeminiAbsenceResearcherTests()
    {
        // Adresinde "missing" geçen kaynak açılamıyor sayılır.
        _links.Setup(x => x.FilterReachableAsync(It.IsAny<IReadOnlyList<AbsenceSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AbsenceSource> s, CancellationToken _) => (IReadOnlyList<AbsenceSource>)s.Where(x => !x.Url.Contains("missing")).ToList());
    }

    private (GeminiAbsenceResearcher Researcher, List<string> Bodies) Create(
        Func<HttpResponseMessage> responder, bool enabled = true, string apiKey = "test-key", int preliminaryDaysAhead = 1)
    {
        var bodies = new List<string>();
        var http = new HttpClient(new StubHandler(responder, bodies)) { BaseAddress = new Uri("https://gemini.test/v1beta/") };
        var ai = new AiOptions();
        ai.Gemini.ApiKey = apiKey;
        ai.Gemini.Model = "gemini-test";
        var researcher = new GeminiAbsenceResearcher(http, new InMemoryRedisCacheService(), Options.Create(ai),
            Options.Create(new AbsencesOptions { Enabled = enabled, CacheMinutes = 240, PreliminaryDaysAhead = preliminaryDaysAhead }), _usage.Object, _links.Object,
            NullLogger<GeminiAbsenceResearcher>.Instance);
        return (researcher, bodies);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ReturnsPending_AndDoesNotSearch_BeyondPreliminaryWindow()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse), preliminaryDaysAhead: 1);

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now.AddDays(2)), CancellationToken.None);

        Assert.Equal(AbsenceReport.Pending, report.State);
        Assert.Empty(bodies);
    }

    [Fact]
    public async Task DayBeforeMatch_SearchesOnce_AsPreliminary()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));
        var match = Match(DateTime.Now.Date.AddDays(1).AddHours(20)); // yarın 20:00

        var report = await researcher.GetAsync(match, CancellationToken.None);
        await researcher.GetAsync(match, CancellationToken.None); // önbellekten

        Assert.Equal(AbsenceReport.Preliminary, report.State);
        Assert.True(report.IsResearched);
        Assert.Equal(3, report.Players.Count);
        var body = Assert.Single(bodies);
        // İstek gövdesi JSON'da Türkçe harfler kaçışlı (\u00E7) yazıldığı için ASCII kısım aranır.
        Assert.Contains("Not: Bug", body);
    }

    [Fact]
    public async Task SeveralDaysBeforeMatch_IsAlsoPreliminary_ByDefault()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse), preliminaryDaysAhead: new AbsencesOptions().PreliminaryDaysAhead);

        var report = await researcher.GetAsync(Match(DateTime.Now.Date.AddDays(3).AddHours(20)), CancellationToken.None);

        Assert.Equal(AbsenceReport.Preliminary, report.State);
        Assert.Contains("Not: Bug", Assert.Single(bodies));
    }

    [Fact]
    public void PreliminaryTtl_IsDailyRefresh_EndingAtMatchDayStart()
    {
        var opt = new AbsencesOptions { PreliminaryCacheHours = 24 };

        Assert.Equal(TimeSpan.FromHours(24), GeminiAbsenceResearcher.PreliminaryTtl(TimeSpan.FromDays(4), opt));
        Assert.Equal(TimeSpan.FromHours(5), GeminiAbsenceResearcher.PreliminaryTtl(TimeSpan.FromHours(5), opt));
        Assert.Equal(TimeSpan.FromMinutes(1), GeminiAbsenceResearcher.PreliminaryTtl(TimeSpan.Zero, opt));
    }

    [Fact]
    public async Task DayBeforeMatch_IsPending_WhenPreliminaryDisabled()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse), preliminaryDaysAhead: 0);

        var report = await researcher.GetAsync(Match(DateTime.Now.Date.AddDays(1).AddHours(20)), CancellationToken.None);

        Assert.Equal(AbsenceReport.Pending, report.State);
        Assert.Empty(bodies);
    }

    [Fact]
    public async Task OnMatchDay_DoesNotSendPreliminaryNote()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        Assert.Equal(AbsenceReport.Ready, report.State);
        Assert.DoesNotContain("Not: Bug", Assert.Single(bodies));
    }

    [Theory]
    [InlineData(false, "test-key")]
    [InlineData(true, "")]
    public async Task ReturnsUnavailable_AndDoesNotSearch_WhenDisabledOrNoKey(bool enabled, string apiKey)
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse), enabled, apiKey);

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        Assert.Equal(AbsenceReport.Unavailable, report.State);
        Assert.Empty(bodies);
    }

    [Fact]
    public async Task OnMatchDay_SearchesWithGoogle_AndParsesPlayersAndSources()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        Assert.Contains("\"google_search\"", Assert.Single(bodies));
        Assert.Equal(AbsenceReport.Ready, report.State);
        // Takım adı fikstüre eşlenir, tanınmayan takım ve tekrar eden oyuncu atılır, tanınmayan durum Şüpheli olur.
        Assert.Collection(report.Players,
            p => Assert.Equal(("Trabzonspor", "Okay Yokuşlu", TeamAbsence.Injured, "Knock"), (p.Team, p.Player, p.Status, p.Reason.En)),
            p => Assert.Equal(("Trabzonspor", "Umut Nayir", TeamAbsence.NotInSquad), (p.Team, p.Player, p.Status)),
            p => Assert.Equal(("CSKA Sofia", "X. Player", TeamAbsence.Doubtful), (p.Team, p.Player, p.Status)));
        Assert.Equal(new[] { "trabzonspor.org.tr", "uefa.com" }, report.Sources.Select(s => s.Title));
        Assert.NotNull(report.CheckedAt);
        // Google kaynakları geldiği için model adresleri denetlenmez.
        _links.Verify(x => x.FilterReachableAsync(It.IsAny<IReadOnlyList<AbsenceSource>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordsUsage_WithSearchToolTokens_AndUniqueNonEmptyQueries()
    {
        var (researcher, _) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));

        await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        // Girdi: 400 + 600 (arama sonuçları); çıktı: 150 + 50 (düşünme). Boş ve tekrar eden (büyük/küçük harf) sorgu sayılmaz.
        _usage.Verify(x => x.RecordSearchAsync(1636402, "gemini-test",
            It.Is<AiCompletion>(c => c.InputTokens == 1000 && c.OutputTokens == 200), 2, null,
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithoutGroundingChunks_UsesModelSources_OnlyIfReachable_AndAssumesOneSearch()
    {
        var (researcher, _) = Create(() => Json(HttpStatusCode.OK, ResponseWithModelSources));

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        Assert.Equal(AbsenceReport.Ready, report.State);
        var source = Assert.Single(report.Sources); // tekrar eden adres bir kez, açılamayan adres hiç gösterilmez
        Assert.Equal(("Kulüp", "https://club.example/news"), (source.Title, source.Url));
        _usage.Verify(x => x.RecordSearchAsync(1636402, "gemini-test", It.IsNotNull<AiCompletion>(), 1, null,
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordsFailedSearch_WithStatusCode_AndNoQueries()
    {
        var (researcher, _) = Create(() => Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"quota"}}"""));

        await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        _usage.Verify(x => x.RecordSearchAsync(1636402, "gemini-test", null, 0, 429,
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipsSearch_WhenMonthlySearchLimitIsReached()
    {
        _usage.Setup(x => x.SearchQueriesThisMonthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(5000); // varsayılan sınır 5000
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        Assert.Equal(AbsenceReport.Unavailable, report.State);
        Assert.Empty(bodies); // Gemini'ye istek gitmedi
        _usage.Verify(x => x.RecordSearchAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<AiCompletion?>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DoesNotRecordUsage_WhenServedFromCache_OrBeforeMatchDay()
    {
        // Ön bilgi kapalı: maç gününden önce arama (ve kullanım kaydı) yok.
        var (researcher, _) = Create(() => Json(HttpStatusCode.OK, GroundedResponse), preliminaryDaysAhead: 0);
        var match = Match(DateTimeOffset.Now);

        await researcher.GetAsync(match, CancellationToken.None);
        await researcher.GetAsync(match, CancellationToken.None);
        await researcher.GetAsync(Match(DateTimeOffset.Now.AddDays(2)), CancellationToken.None);

        _usage.Verify(x => x.RecordSearchAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<AiCompletion?>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CachesResult_SoSearchRunsOncePerFixture()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));
        var match = Match(DateTimeOffset.Now);

        await researcher.GetAsync(match, CancellationToken.None);
        await researcher.GetAsync(match, CancellationToken.None);

        Assert.Single(bodies);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"Search grounding is not supported on the free tier"}}""")]
    [InlineData(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"Bilgi bulamadım."}]}}]}""")]
    public async Task ReturnsUnavailable_OnErrorOrInvalidAnswer(HttpStatusCode code, string body)
    {
        var (researcher, _) = Create(() => Json(code, body));

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        Assert.Equal(AbsenceReport.Unavailable, report.State);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> responder, List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return responder();
        }
    }

    // ---------- Hakem ----------

    private static JsonElement Referee(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ParseReferee_ReadsStats_ComputesStrictness_AndDropsImplausibleNumbers()
    {
        var r = GeminiAbsenceResearcher.ParseReferee(Referee("""
            { "name": "Halil Umut Meler", "matches": "12", "yellowPerMatch": 5.3, "redPerMatch": 0.2,
              "penaltiesPerMatch": 0.3, "foulsPerMatch": 140, "style": "Kartı erken çıkarır.", "styleEn": "Books early." }
            """), officialName: null);

        Assert.NotNull(r);
        Assert.Equal(("Halil Umut Meler", 12, 5.3, 0.2, 0.3), (r!.Name, r.Matches, r.YellowPerMatch, r.RedPerMatch, r.PenaltiesPerMatch));
        Assert.Null(r.FoulsPerMatch); // maç başı 140 faul makul değil
        Assert.Equal(RefereeInfo.Strict, r.Strictness);
        Assert.Equal("Books early.", r.Style!.En);
    }

    [Fact]
    public void ParseReferee_KeepsOfficialName_AndDropsStats_WhenModelFoundAnotherPerson()
    {
        var same = GeminiAbsenceResearcher.ParseReferee(Referee("""{ "name": "H. U. Meler", "yellowPerMatch": 4.0 }"""), "Halil Umut Meler");
        var other = GeminiAbsenceResearcher.ParseReferee(Referee("""{ "name": "Cüneyt Çakır", "yellowPerMatch": 4.0 }"""), "Halil Umut Meler");

        Assert.Equal(("Halil Umut Meler", 4.0, RefereeInfo.Balanced), (same!.Name, same.YellowPerMatch, same.Strictness));
        Assert.Equal("Halil Umut Meler", other!.Name);
        Assert.Null(other.YellowPerMatch);
    }

    [Fact]
    public void ParseReferee_ReturnsNull_WhenNothingFound()
    {
        Assert.Null(GeminiAbsenceResearcher.ParseReferee(null, null));
        Assert.Null(GeminiAbsenceResearcher.ParseReferee(Referee("null"), null));
        // API-Football adı biliyorsa model bulamasa da ad gösterilir (istatistiksiz).
        var nameOnly = GeminiAbsenceResearcher.ParseReferee(Referee("null"), "Halil Umut Meler");
        Assert.Equal(("Halil Umut Meler", (double?)null), (nameOnly!.Name, nameOnly.YellowPerMatch));
    }

    [Fact]
    public async Task SendsOfficialRefereeName_InUserPrompt()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));

        await researcher.GetAsync(Match(DateTimeOffset.Now) with { Referee = "Halil Umut Meler" }, CancellationToken.None);

        Assert.Contains("Hakem (resmi atama): Halil Umut Meler", Assert.Single(bodies));
    }

    [Fact]
    public void ParseForm_MapsTeams_NormalizesLast5_AndDropsImplausibleNumbers()
    {
        var form = GeminiAbsenceResearcher.ParseForm(Referee("""
            [ { "team": "Trabzonspor A.Ş.", "last5": "G, G, B, M, G", "leaguePosition": 3, "venueMatches": "7",
                "venueGoalsFor": 1.4, "venueGoalsAgainst": 1.1, "xgFor": 1.3, "xgAgainst": 9.0 },
              { "team": "CSKA Sofia", "last5": "WWDLW", "leaguePosition": 2, "venueMatches": 8, "venueGoalsFor": 2.1,
                "venueGoalsAgainst": 0.6, "xgFor": null, "xgAgainst": null },
              { "team": "Başka Takım", "last5": "WWWWW" } ]
            """), Match(DateTimeOffset.Now));

        // Ev sahibi önce; tanınmayan takım atılır.
        Assert.Equal(new[] { "CSKA Sofia", "Trabzonspor" }, form.Select(f => f.Team));
        var away = form[1];
        Assert.Equal(("WWDLW", 3, 7, 1.4, 1.1, 1.3), (away.Last5, away.LeaguePosition, away.VenueMatches, away.VenueGoalsFor, away.VenueGoalsAgainst, away.XgFor));
        Assert.Null(away.XgAgainst); // maç başı 9 xG makul değil
        Assert.Null(form[0].XgFor);
    }

    [Fact]
    public void ParseForm_SkipsTeamsWithoutData_AndInvalidLast5()
    {
        var form = GeminiAbsenceResearcher.ParseForm(Referee("""
            [ { "team": "Trabzonspor", "last5": "WWXLW", "leaguePosition": 0 },
              { "team": "CSKA Sofia", "last5": "WWDLWDL" } ]
            """), Match(DateTimeOffset.Now));

        Assert.Empty(form);
        Assert.Empty(GeminiAbsenceResearcher.ParseForm(null, Match(DateTimeOffset.Now)));
        Assert.Empty(GeminiAbsenceResearcher.ParseForm(Referee("null"), Match(DateTimeOffset.Now)));
    }

    [Fact]
    public void ParseForm_KeepsLastMatchDate_OnlyIfBeforeMatchAndWithin30Days()
    {
        var match = Match(DateTimeOffset.Now);
        var day = DateOnly.FromDateTime(match.Kickoff.ToLocalTime().DateTime);
        string D(int offset) => day.AddDays(offset).ToString("yyyy-MM-dd");
        var form = GeminiAbsenceResearcher.ParseForm(Referee($$"""
            [ { "team": "CSKA Sofia", "lastMatchDate": "{{D(-3)}}", "lastMatchCompetition": "Bulgarian Cup" },
              { "team": "Trabzonspor", "last5": "WDW", "lastMatchDate": "{{D(0)}}", "lastMatchCompetition": "Süper Lig" } ]
            """), match);

        // Yalnızca tarihi olan takım da listede kalır (dinlenme için).
        Assert.Equal((day.AddDays(-3), "Bulgarian Cup"), (form[0].LastMatchDate, form[0].LastMatchCompetition));
        // Maç günüyle aynı tarih geçersiz: tarih ve turnuva atılır, form verisi kalır.
        Assert.Equal(("WDW", (DateOnly?)null, (string?)null), (form[1].Last5, form[1].LastMatchDate, form[1].LastMatchCompetition));

        var old = GeminiAbsenceResearcher.ParseForm(Referee($$"""[ { "team": "CSKA Sofia", "lastMatchDate": "{{D(-31)}}" }, { "team": "Trabzonspor", "lastMatchDate": "26.11.2026" } ]"""), match);
        Assert.Empty(old); // 30 günden eski ve biçimi yanlış tarih → takımın başka verisi yok
    }

    [Fact]
    public async Task OnMatchDay_ReadsForm_FromSameSearch()
    {
        const string body = """
            { "candidates": [{ "content": { "parts": [ { "text": "{\"players\":[],\"form\":[{\"team\":\"Trabzonspor\",\"last5\":\"WDW\",\"leaguePosition\":4}]}" } ] } }] }
            """;
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, body));

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now), CancellationToken.None);

        Assert.Single(bodies); // ek arama yok
        var f = Assert.Single(report.Form!);
        Assert.Equal(("Trabzonspor", "WDW", 4), (f.Team, f.Last5, f.LeaguePosition));
    }
}