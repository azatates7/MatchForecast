using System.Text.Json;
using Confluent.Kafka;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Kafka;
using MatchForecast.Data.Configuration;
using MatchForecast.Models.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MatchForecast.Tests;

/*
 * Consumer'ın Kafka'dan bağımsız kısmı: mesaj gövdesinin okunması ve DB'ye yazılması. Consume döngüsü gerçek bir broker
 * ister; o kısım docker compose ile elle doğrulanır (README'deki "Kafka" bölümü).
 */
public class ForecastPersistenceConsumerTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private ForecastPersistenceConsumer CreateConsumer()
    {
        var services = new ServiceCollection()
            .AddScoped(_ => new AppDbContext(_dbOptions))
            .AddSingleton(Options.Create(new AiOptions { Provider = "Ollama" }))
            .AddScoped<ForecastStore>()
            .BuildServiceProvider();

        return new ForecastPersistenceConsumer(
            services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IProducer<int, string>>(),
            Options.Create(new KafkaOptions { Enabled = true }),
            NullLogger<ForecastPersistenceConsumer>.Instance);
    }

    [Fact]
    public void Deserialize_RoundTripsPublishedMessage()
    {
        var original = KafkaForecastPublisherTests.Forecast(42);
        var json = JsonSerializer.Serialize(original, KafkaClientFactory.JsonOptions);

        var restored = ForecastPersistenceConsumer.Deserialize(json);

        Assert.Equal(original.Match, restored.Match);
        Assert.Equal(original.Summary, restored.Summary);
        Assert.Equal(original.Outcomes, restored.Outcomes);
        Assert.Equal(StatMetric.Corners, Assert.Single(restored.Stats).Metric);
        Assert.Equal("MS 1", Assert.Single(restored.Predictions).Label.Tr);
        Assert.Equal(original.GeneratedAt, restored.GeneratedAt);
    }

    [Fact]
    public void Deserialize_Throws_OnInvalidJson()
    {
        // Bozuk mesaj tekrar denenmez, DLQ'ya gider; bunun için JsonException fırlatılması gerekir.
        Assert.ThrowsAny<JsonException>(() => ForecastPersistenceConsumer.Deserialize("not json"));
        Assert.ThrowsAny<JsonException>(() => ForecastPersistenceConsumer.Deserialize("null"));
    }

    [Fact]
    public void Deserialize_Throws_WhenMatchIsMissing()
    {
        // Geçerli ama maçsız JSON da bozuk mesajdır: DLQ'ya gitmeli, kayıtta NullReferenceException ile partition'ı kilitlememeli.
        Assert.ThrowsAny<JsonException>(() => ForecastPersistenceConsumer.Deserialize("{}"));

        var json = JsonSerializer.Serialize(KafkaForecastPublisherTests.Forecast(5) with { Match = null! }, KafkaClientFactory.JsonOptions);
        Assert.ThrowsAny<JsonException>(() => ForecastPersistenceConsumer.Deserialize(json));
    }

    [Fact]
    public async Task PersistAsync_IsIdempotent_SingleRowPerFixture()
    {
        // At-least-once teslimde aynı mesaj iki kez gelebilir; sonuç yine tek satır olmalı.
        var consumer = CreateConsumer();
        var forecast = KafkaForecastPublisherTests.Forecast(9);

        await consumer.PersistAsync(forecast, CancellationToken.None);
        await consumer.PersistAsync(forecast, CancellationToken.None);

        await using var db = new AppDbContext(_dbOptions);
        var record = Assert.Single(await db.Forecasts.ToListAsync());
        Assert.Equal(9, record.FixtureId);
        Assert.Equal("Galatasaray", record.HomeTeam);
    }
}