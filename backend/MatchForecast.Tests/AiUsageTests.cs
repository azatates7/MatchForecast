using System.Net;
using System.Net.Http.Json;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MatchForecast.Tests;

// AI kullanım kaydı (maliyet hesabı) ve yalnızca yetkiliye açık rapor endpoint'i.
public class AiUsageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task RecordAsync_SavesTokensAndCost_WithCurrentProviderPrices()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ai = new AiOptions { Provider = "Gemini" };
        ai.Gemini.Model = "test-flash";
        ai.Gemini.InputPricePer1M = 0.30m;
        ai.Gemini.OutputPricePer1M = 2.50m;

        await using (var db = new AppDbContext(options))
            await new AiUsageStore(db, Options.Create(ai), Options.Create(new AbsencesOptions()))
                .RecordAsync(7, new AiCompletion("{}", 1_000, 2_000), null, TimeSpan.FromMilliseconds(1500), CancellationToken.None);

        await using var check = new AppDbContext(options);
        var usage = await check.AiUsages.SingleAsync();
        Assert.Equal(("Gemini", "test-flash"), (usage.Provider, usage.Model));
        Assert.Equal(AiUsagePurpose.Forecast, usage.Purpose);
        Assert.Equal(0, usage.SearchQueries);
        Assert.True(usage.Success);
        Assert.Equal(1_000, usage.InputTokens);
        Assert.Equal(2_000, usage.OutputTokens);
        Assert.Equal(1500, usage.DurationMs);
        // 1000 * 0.30 / 1M + 2000 * 2.50 / 1M = 0.0003 + 0.005
        Assert.Equal(0.0053m, usage.EstimatedCostUsd);
    }

    [Fact]
    public async Task RecordAsync_SavesFailure_WithStatusCodeAndZeroCost()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ai = new AiOptions { Provider = "Gemini" };
        ai.Gemini.InputPricePer1M = 0.30m;

        await using (var db = new AppDbContext(options))
            await new AiUsageStore(db, Options.Create(ai), Options.Create(new AbsencesOptions())).RecordAsync(7, null, 429, TimeSpan.FromSeconds(1), CancellationToken.None);

        await using var check = new AppDbContext(options);
        var usage = await check.AiUsages.SingleAsync();
        Assert.False(usage.Success);
        Assert.Equal(429, usage.StatusCode);
        Assert.Null(usage.InputTokens);
        Assert.Equal(0m, usage.EstimatedCostUsd);
    }

    [Fact]
    public async Task RecordSearchAsync_UsesGeminiPrices_AndChargesOnlySearchesBeyondMonthlyFreeQuota()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        // Tahmin sağlayıcısı Claude olsa da arama Gemini ile yapılır ve Gemini fiyatıyla hesaplanır.
        var ai = new AiOptions { Provider = "Claude" };
        ai.Gemini.InputPricePer1M = 0.30m;
        ai.Gemini.OutputPricePer1M = 2.50m;
        var absences = new AbsencesOptions { SearchPricePer1K = 14m, FreeSearchesPerMonth = 10 };

        await using (var db = new AppDbContext(options))
        {
            // Bu ay 8 sorgu yapılmış (kota 10); geçen ayın sorguları sayılmaz.
            db.AiUsages.AddRange(
                new AiUsage { Purpose = AiUsagePurpose.Absences, SearchQueries = 8, CreatedAt = DateTime.Now },
                new AiUsage { Purpose = AiUsagePurpose.Absences, SearchQueries = 50, CreatedAt = DateTime.Now.AddMonths(-1).AddDays(-1) });
            await db.SaveChangesAsync();

            var store = new AiUsageStore(db, Options.Create(ai), Options.Create(absences));
            // 5 sorgunun 2'si kotada, 3'ü ücretli. Ardından 4 sorgunun hepsi ücretli.
            await store.RecordSearchAsync(7, "gemini-search", new AiCompletion("", 1_000, 2_000), 5, null, TimeSpan.FromSeconds(3), CancellationToken.None);
            await store.RecordSearchAsync(8, "gemini-search", new AiCompletion("", null, null), 4, null, TimeSpan.FromSeconds(3), CancellationToken.None);
        }

        await using var check = new AppDbContext(options);
        var first = await check.AiUsages.SingleAsync(x => x.FixtureId == 7);
        Assert.Equal((AiUsagePurpose.Absences, "Gemini", "gemini-search", 5), (first.Purpose, first.Provider, first.Model, first.SearchQueries));
        // Token: 0.0003 + 0.005; arama: 3 * 14 / 1000 = 0.042
        Assert.Equal(0.0473m, first.EstimatedCostUsd);
        var second = await check.AiUsages.SingleAsync(x => x.FixtureId == 8);
        Assert.Equal(0.056m, second.EstimatedCostUsd);
    }

    [Fact]
    public async Task Report_SeparatesAbsenceSearches_AndSumsSearchQueries()
    {
        var testFactory = factory.WithTestJwt();
        var marker = DateTime.Now.AddDays(-2);
        using (var scope = testFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var search = Usage(marker, success: true, input: 300, output: 100, cost: 0.014m);
            search.Purpose = AiUsagePurpose.Absences;
            search.SearchQueries = 3;
            db.AiUsages.AddRange(Usage(marker, success: true, input: 800, output: 600, cost: 0.002m), search);
            await db.SaveChangesAsync();
        }

        var admin = testFactory.CreateClientFor(1, [RoleNames.Admin]);
        var report = (await admin.GetFromJsonAsync<AiUsageReportResponse>("/api/ai-usage?days=30"))!;

        var day = report.Daily.Where(x => x.Date == DateOnly.FromDateTime(marker)).ToList();
        Assert.Contains(day, x => x.Purpose == AiUsagePurpose.Absences && x.SearchQueries == 3 && x.Calls == 1);
        Assert.Contains(day, x => x.Purpose == AiUsagePurpose.Forecast && x.SearchQueries == 0);
        Assert.Equal(3, report.Totals.SearchQueries);
    }

    [Fact]
    public async Task Report_ReturnsTotalsAndDailyRows_ForAdmin_AndSkipsOldRows()
    {
        var testFactory = factory.WithTestJwt();
        using (var scope = testFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiUsages.AddRange(
                Usage(DateTime.Now, success: true, input: 800, output: 600, cost: 0.002m),
                Usage(DateTime.Now, success: false, input: null, output: null, cost: 0m),
                Usage(DateTime.Now.AddDays(-1), success: true, input: 700, output: 500, cost: 0.001m),
                Usage(DateTime.Now.AddDays(-40), success: true, input: 9_999, output: 9_999, cost: 9m)); // 30 günün dışında
            await db.SaveChangesAsync();
        }

        var admin = testFactory.CreateClientFor(1, [RoleNames.Admin]);
        var response = await admin.GetAsync("/api/ai-usage?days=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<AiUsageReportResponse>())!;
        Assert.Equal(3, report.Totals.Calls);
        Assert.Equal(1, report.Totals.Failures);
        Assert.Equal(1_500, report.Totals.InputTokens);
        Assert.Equal(1_100, report.Totals.OutputTokens);
        Assert.Equal(0.003m, report.Totals.EstimatedCostUsd);
        Assert.Equal(2, report.Daily.Count);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), report.Daily[0].Date); // en yeni gün önce
        Assert.Equal(2, report.Daily[0].Calls);
    }

    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.Guest)]
    public async Task Report_IsForbidden_WithoutPermission(string role)
    {
        var client = factory.WithTestJwt().CreateClientFor(2, [role]);

        var response = await client.GetAsync("/api/ai-usage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Report_IsAllowed_WithAiUsageViewPermission()
    {
        var client = factory.WithTestJwt().CreateClientFor(3, [RoleNames.User], PermissionKeys.AiUsageView);

        var response = await client.GetAsync("/api/ai-usage?days=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static AiUsage Usage(DateTime createdAt, bool success, int? input, int? output, decimal cost) => new()
    {
        FixtureId = 1,
        Provider = "Gemini",
        Model = "test-flash",
        Success = success,
        StatusCode = success ? null : 429,
        InputTokens = input,
        OutputTokens = output,
        DurationMs = 1000,
        EstimatedCostUsd = cost,
        CreatedAt = createdAt
    };
}