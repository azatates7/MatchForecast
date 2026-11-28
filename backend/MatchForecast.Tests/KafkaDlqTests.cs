using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MatchForecast.Api.Services.Kafka;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MatchForecast.Tests;

// DLQ yönetim uçları (Kafka testlerde kapalı) ve DLQ mesajının panelde nasıl özetlendiği.
public class KafkaDlqTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Report_SaysDisabled_WhenKafkaIsOff()
    {
        var client = factory.CreateAuthorizedClient(RoleNames.Admin);

        var report = await client.GetFromJsonAsync<KafkaDlqReportResponse>("/api/admin/kafka/dlq");

        Assert.False(report!.Enabled);
        Assert.Empty(report.Messages);
    }

    [Fact]
    public async Task Replay_ReturnsConflict_WhenKafkaIsOff()
    {
        var client = factory.CreateAuthorizedClient(RoleNames.Admin);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/admin/kafka/dlq/0/5/replay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/admin/kafka/dlq/replay-all", null)).StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.Guest)]
    public async Task Endpoints_AreForbidden_WithoutPermission(string role)
    {
        var client = factory.CreateAuthorizedClient(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/kafka/dlq")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/admin/kafka/dlq/replay-all", null)).StatusCode);
    }

    [Fact]
    public void Describe_ReadsMatchFromForecastMessage()
    {
        var match = new MatchSummary(42, new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(3)), "Süper Lig", "Turkey", "Galatasaray", "Fenerbahçe", "NS");
        var forecast = new ForecastResult(match, LocalizedText.Of("ozet", "summary"), null!, [], [], DateTimeOffset.Now);
        var json = JsonSerializer.Serialize(forecast, KafkaClientFactory.JsonOptions);

        var (label, kickoff, preview) = KafkaDlqService.Describe(json);

        Assert.Equal("Galatasaray – Fenerbahçe", label);
        Assert.Equal(match.Kickoff, kickoff);
        Assert.Null(preview);
    }

    [Fact]
    public void Describe_ShowsPreview_ForBrokenMessage()
    {
        var (label, _, preview) = KafkaDlqService.Describe("{ bozuk");

        Assert.Null(label);
        Assert.Equal("{ bozuk", preview);
    }
}