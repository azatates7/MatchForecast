using MatchForecast.Api.Services.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MatchForecast.Tests;

// Sağlık izleme: durum değişikliği ancak ardışık kontrollerde aynı kalınca bildirilir; düzelince ayrıca bildirilir.
public class HealthMonitorTests
{
    private static Dictionary<string, (HealthStatus, string?)> State(HealthStatus kafka, HealthStatus redis = HealthStatus.Healthy) =>
        new() { ["database"] = (HealthStatus.Healthy, null), ["redis"] = (redis, null), ["kafka"] = (kafka, "Kafka'ya ulaşılamıyor") };

    [Fact]
    public void ReportsChange_OnlyAfterConsecutiveChecks()
    {
        var tracker = new HealthStateTracker(2);

        Assert.Empty(tracker.Update(State(HealthStatus.Degraded)));              // 1. kontrol: henüz değil
        var change = Assert.Single(tracker.Update(State(HealthStatus.Degraded))); // 2. kontrol: onaylandı

        Assert.Equal(("kafka", HealthStatus.Healthy, HealthStatus.Degraded), (change.Name, change.From, change.To));
        Assert.Empty(tracker.Update(State(HealthStatus.Degraded)));              // aynı sorun tekrar bildirilmez
    }

    [Fact]
    public void IgnoresShortFlaps()
    {
        var tracker = new HealthStateTracker(2);

        Assert.Empty(tracker.Update(State(HealthStatus.Degraded)));
        Assert.Empty(tracker.Update(State(HealthStatus.Healthy)));  // tek seferlik dalgalanma
        Assert.Empty(tracker.Update(State(HealthStatus.Degraded))); // sayaç baştan başlar
    }

    [Fact]
    public void ReportsRecovery()
    {
        var tracker = new HealthStateTracker(1);
        tracker.Update(State(HealthStatus.Degraded));

        var change = Assert.Single(tracker.Update(State(HealthStatus.Healthy)));

        Assert.Equal((HealthStatus.Degraded, HealthStatus.Healthy), (change.From, change.To));
        Assert.Equal("[MatchForecast][Production] Düzeldi: Kafka", HealthMonitorService.Subject(change, "Production"));
    }

    [Fact]
    public void Subject_DescribesProblem()
    {
        var down = new HealthStateTracker.Change("database", HealthStatus.Healthy, HealthStatus.Unhealthy, null);
        var partial = new HealthStateTracker.Change("redis", HealthStatus.Healthy, HealthStatus.Degraded, null);

        Assert.Equal("[MatchForecast][Production] Sorun: Veritabanı çalışmıyor", HealthMonitorService.Subject(down, "Production"));
        Assert.Equal("[MatchForecast][Production] Sorun: Redis kısmen çalışıyor", HealthMonitorService.Subject(partial, "Production"));
    }
}