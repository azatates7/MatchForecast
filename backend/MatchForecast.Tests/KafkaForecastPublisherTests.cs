using System.Text;
using Confluent.Kafka;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Kafka;
using MatchForecast.Data.Configuration;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MatchForecast.Tests;

// Kafka açıkken tahmin topic'e yazılır; Kafka'ya ulaşılamazsa eskisi gibi doğrudan DB'ye düşer.
public class KafkaForecastPublisherTests
{
    private const string Topic = "test.forecast.generated";

    private readonly Mock<IProducer<int, string>> _producer = new();
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private KafkaForecastPublisher CreatePublisher() => new(
        _producer.Object,
        new ForecastStore(_db, Options.Create(new AiOptions { Provider = "Ollama" })),
        Options.Create(new KafkaOptions { Enabled = true, ForecastGeneratedTopic = Topic }),
        NullLogger<KafkaForecastPublisher>.Instance);

    internal static ForecastResult Forecast(int fixtureId) => new(
        new MatchSummary(fixtureId, new DateTimeOffset(2026, 11, 14, 19, 0, 0, TimeSpan.FromHours(3)), "Süper Lig", "Turkey", "Galatasaray", "Fenerbahçe", "NS"),
        new LocalizedText("Ev sahibi önde.", "Home side ahead."),
        new OutcomeProbabilities(50, 25, 25, 55, 40, 35, 25),
        [new StatProjection(StatMetric.Corners, new LocalizedText("Korner", "Corners"), 10.2m, 6, 14, [], new LocalizedText("Kanat oyunu.", "Wing play."))],
        [new Prediction(1, new LocalizedText("MS 1", "FT 1"), "1X2", "1", 61, new LocalizedText("Form.", "Form."))],
        new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(3)));

    [Fact]
    public async Task SaveAsync_PublishesToTopicWithFixtureKey_AndDoesNotWriteDatabase()
    {
        Message<int, string>? sent = null;
        _producer.Setup(p => p.ProduceAsync(Topic, It.IsAny<Message<int, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<int, string>, CancellationToken>((_, m, _) => sent = m)
            .ReturnsAsync(new DeliveryResult<int, string> { Topic = Topic, Partition = new Partition(0), Offset = new Offset(1) });

        await CreatePublisher().SaveAsync(Forecast(42), CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal(42, sent!.Key);
        Assert.Equal(KafkaClientFactory.ForecastGeneratedEventType,
            Encoding.UTF8.GetString(sent.Headers.GetLastBytes(KafkaClientFactory.EventTypeHeader)));
        Assert.Equal(42, ForecastPersistenceConsumer.Deserialize(sent.Value).Match.Id);
        Assert.Empty(await _db.Forecasts.ToListAsync()); // DB'ye consumer yazar, publisher değil.
    }

    [Fact]
    public async Task SaveAsync_FallsBackToDatabase_WhenKafkaUnavailable()
    {
        _producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<int, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<int, string>(new Error(ErrorCode.Local_MsgTimedOut), new DeliveryResult<int, string>()));

        await CreatePublisher().SaveAsync(Forecast(7), CancellationToken.None);

        var record = Assert.Single(await _db.Forecasts.ToListAsync());
        Assert.Equal(7, record.FixtureId);
    }
}