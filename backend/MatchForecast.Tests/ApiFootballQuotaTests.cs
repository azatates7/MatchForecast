using System.Net;
using System.Text;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Models.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MatchForecast.Tests;

// API-Football günlük kota takibi ve beklenen API-Football hatalarının 4xx'e çevrilmesi.
public class ApiFootballQuotaTests
{
    private readonly InMemoryRedisCacheService _cache = new();
    private readonly FakeEmailSender _email = new();

    private ApiFootballQuotaTracker CreateTracker(bool smtpEnabled = true)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Testing");
        return new ApiFootballQuotaTracker(_cache, _email,
            Options.Create(new OddsProviderOptions { QuotaWarnPercent = 20 }),
            Options.Create(new SmtpOptions { Enabled = smtpEnabled, AdminEmails = ["admin@test.local"] }),
            env.Object, NullLogger<ApiFootballQuotaTracker>.Instance);
    }

    [Theory]
    [InlineData(100, 50, null)]
    [InlineData(100, 21, null)]
    [InlineData(100, 20, "low")]
    [InlineData(100, 1, "low")]
    [InlineData(100, 0, "empty")]
    public void WarningLevel_FollowsThreshold(int limit, int remaining, string? expected)
    {
        Assert.Equal(expected, ApiFootballQuotaTracker.WarningLevel(limit, remaining, 20));
    }

    [Fact]
    public void WarningLevel_IsOff_WhenPercentIsZero()
    {
        Assert.Null(ApiFootballQuotaTracker.WarningLevel(100, 0, 0));
    }

    [Fact]
    public async Task Record_StoresLatestQuota()
    {
        var tracker = CreateTracker();

        await tracker.RecordAsync(100, 63, DateTimeOffset.Now);

        var quota = await tracker.GetAsync();
        Assert.Equal((100, 63), (quota!.Limit, quota.Remaining));
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Record_WarnsOncePerDayPerLevel()
    {
        var tracker = CreateTracker();
        var now = DateTimeOffset.Now;

        await tracker.RecordAsync(100, 20, now);
        await tracker.RecordAsync(100, 15, now); // aynı gün, aynı seviye: yeni mail yok
        await tracker.RecordAsync(100, 0, now);  // kota bitti: ayrı mail

        await WaitForMailsAsync(2);
        Assert.Equal(2, _email.Sent.Count);
        Assert.Contains(_email.Sent, m => m.Subject.Contains("20/100 kaldı"));
        Assert.Contains(_email.Sent, m => m.Subject.Contains("doldu"));
    }

    [Fact]
    public async Task Record_DoesNotMail_WhenSmtpDisabled()
    {
        var tracker = CreateTracker(smtpEnabled: false);

        await tracker.RecordAsync(100, 0, DateTimeOffset.Now);

        await Task.Delay(100);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Handler_ReadsQuotaHeaders_FromResponses()
    {
        var tracker = CreateTracker();
        var handler = new ApiFootballQuotaHandler(tracker, NullLogger<ApiFootballQuotaHandler>.Instance)
        {
            InnerHandler = new StubHandler(() =>
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                res.Headers.Add(ApiFootballQuotaTracker.LimitHeader, "100");
                res.Headers.Add(ApiFootballQuotaTracker.RemainingHeader, "42");
                return res;
            })
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };

        await http.GetAsync("fixtures?date=2026-11-28");

        Assert.Equal(42, (await tracker.GetAsync())!.Remaining);
    }

    [Theory]
    [InlineData("""{"plan":"Free plans do not have access to this date, try from 2026-11-27 to 2026-11-29."}""", 400)]
    [InlineData("""{"requests":"You have reached the request limit for the day."}""", 429)]
    [InlineData("""{"rateLimit":"Too many requests."}""", 429)]
    [InlineData("""{"token":"Error/Missing application key."}""", 502)]
    public async Task Provider_MapsKnownApiFootballErrors(string errors, int expectedStatus)
    {
        var body = $$"""{ "errors": {{errors}}, "response": [] }""";
        var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })) { BaseAddress = new Uri("https://api.test/") };
        var provider = new ApiFootballOddsProvider(http, _cache, Options.Create(new OddsProviderOptions()), NullLogger<ApiFootballOddsProvider>.Instance);

        var ex = await Assert.ThrowsAsync<ForecastException>(() => provider.GetMatchesAsync(new DateOnly(2026, 11, 30), CancellationToken.None));

        Assert.Equal(expectedStatus, ex.StatusCode);
    }

    private async Task WaitForMailsAsync(int count)
    {
        for (var i = 0; i < 50 && _email.Sent.Count < count; i++)
            await Task.Delay(20);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(responder());
    }
}