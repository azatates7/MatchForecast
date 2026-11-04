using System.Text.Json;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Data.Configuration;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace MatchForecast.Tests;

// Tahminler maç başına tek satır olarak saklanır; yeniden üretilince satır güncellenir.
public class ForecastStoreTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private ForecastStore CreateStore(string provider = "Ollama") => new(
        _db,
        Options.Create(new AiOptions { Provider = provider, Ollama = new OllamaOptions { Model = "qwen3:8b" } }));

    private static ForecastResult Forecast(int fixtureId, string summary, DateTimeOffset generatedAt) => new(
        new MatchSummary(fixtureId, new DateTimeOffset(2026, 11, 4, 19, 0, 0, TimeSpan.FromHours(3)), "Süper Lig", "Turkey", "Galatasaray", "Fenerbahçe", "NS"),
        new LocalizedText(summary, summary),
        new OutcomeProbabilities(50, 25, 25, 55, null, null, null),
        [],
        [],
        generatedAt);

    [Fact]
    public async Task SaveAsync_InsertsThenUpdates_SingleRowPerFixture()
    {
        var store = CreateStore();
        var firstAt = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(3));

        await store.SaveAsync(Forecast(42, "ilk", firstAt), CancellationToken.None);
        await store.SaveAsync(Forecast(42, "ikinci", firstAt.AddHours(1)), CancellationToken.None);

        var record = Assert.Single(await _db.Forecasts.ToListAsync());
        Assert.Equal(42, record.FixtureId);
        Assert.Equal("Galatasaray", record.HomeTeam);
        Assert.Equal(new DateTime(2026, 11, 4, 19, 0, 0), record.Kickoff); // İstanbul saati
        Assert.Equal(new DateTime(2026, 11, 4, 11, 0, 0), record.GeneratedAt);
        Assert.NotNull(record.UpdatedAt);

        var saved = JsonSerializer.Deserialize<ForecastResult>(record.ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("ikinci", saved!.Summary.Tr);
    }

    [Fact]
    public async Task SaveAsync_StoresProviderAndModel()
    {
        await CreateStore("Ollama").SaveAsync(Forecast(1, "x", DateTimeOffset.Now), CancellationToken.None);

        var record = Assert.Single(await _db.Forecasts.ToListAsync());
        Assert.Equal("Ollama", record.AiProvider);
        Assert.Equal("qwen3:8b", record.AiModel);
    }
}