using System.Net;
using System.Text;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Absences;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MatchForecast.Tests;

// Maç günü kuralı, Google araması isteği, cevabın ayrıştırılması (takım eşleme, durum normalizasyonu, kaynaklar) ve fail-open.
public class GeminiAbsenceResearcherTests
{
    private static MatchSummary Match(DateTimeOffset kickoff) =>
        new(1636402, kickoff, "UEFA Europa Conference League", "World", "CSKA Sofia", "Trabzonspor", "NS");

    private const string GroundedResponse = """
        {
          "candidates": [{
            "content": { "parts": [ { "text": "```json\n{\"players\":[{\"team\":\"Trabzonspor\",\"player\":\"Okay Yokuşlu\",\"status\":\"Injured\",\"reason\":\"Darbe\",\"reasonEn\":\"Knock\"},{\"team\":\"Trabzonspor A.Ş.\",\"player\":\"Umut Nayir\",\"status\":\"NotInSquad\",\"reason\":\"UEFA listesinde yok\",\"reasonEn\":\"Not in UEFA list\"},{\"team\":\"CSKA Sofia\",\"player\":\"X. Player\",\"status\":\"maybe\",\"reason\":\"Belirsiz\"},{\"team\":\"Başka Takım\",\"player\":\"Y\",\"status\":\"Injured\"},{\"team\":\"Trabzonspor\",\"player\":\"okay yokuşlu\",\"status\":\"Injured\"}]}\n```" } ] },
            "groundingMetadata": { "groundingChunks": [
              { "web": { "uri": "https://vertexaisearch.example/a", "title": "trabzonspor.org.tr" } },
              { "web": { "uri": "https://vertexaisearch.example/b", "title": "uefa.com" } },
              { "web": { "uri": "https://vertexaisearch.example/c", "title": "trabzonspor.org.tr" } } ] }
          }]
        }
        """;

    private static (GeminiAbsenceResearcher Researcher, List<string> Bodies) Create(
        Func<HttpResponseMessage> responder, bool enabled = true, string apiKey = "test-key")
    {
        var bodies = new List<string>();
        var http = new HttpClient(new StubHandler(responder, bodies)) { BaseAddress = new Uri("https://gemini.test/v1beta/") };
        var ai = new AiOptions();
        ai.Gemini.ApiKey = apiKey;
        ai.Gemini.Model = "gemini-test";
        var researcher = new GeminiAbsenceResearcher(http, new InMemoryRedisCacheService(), Options.Create(ai),
            Options.Create(new AbsencesOptions { Enabled = enabled, CacheMinutes = 240 }), NullLogger<GeminiAbsenceResearcher>.Instance);
        return (researcher, bodies);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ReturnsPending_AndDoesNotSearch_BeforeMatchDay()
    {
        var (researcher, bodies) = Create(() => Json(HttpStatusCode.OK, GroundedResponse));

        var report = await researcher.GetAsync(Match(DateTimeOffset.Now.AddDays(2)), CancellationToken.None);

        Assert.Equal(AbsenceReport.Pending, report.State);
        Assert.Empty(bodies);
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
}