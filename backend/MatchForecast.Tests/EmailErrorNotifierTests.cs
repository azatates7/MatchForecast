using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Email;
using MatchForecast.Logger.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MatchForecast.Tests;

public class EmailErrorNotifierTests
{
    private readonly Mock<IEmailSender> _sender = new();

    private EmailErrorNotifier CreateNotifier(bool enabled = true)
    {
        var options = Options.Create(new SmtpOptions { Enabled = enabled, AdminEmails = ["admin@example.com"], ThrottleMinutes = 10 });
        var env = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Test");
        return new EmailErrorNotifier(_sender.Object, options, env, NullLogger<EmailErrorNotifier>.Instance);
    }

    private static ErrorNotification Error(string path, string message = "AI servisi şu an yoğun.") =>
        new(DateTimeOffset.Now, "GET", path, null, 503, message, "MatchForecast.Models.Common.ForecastException", null, "trace-1");

    // Arka plan kuyruğu asenkron işlediği için beklenen çağrı sayısına ulaşılana kadar kısa süre beklenir.
    private async Task WaitForSendsAsync(int expected)
    {
        for (var i = 0; i < 50 && _sender.Invocations.Count < expected; i++)
            await Task.Delay(20);
        await Task.Delay(100);
    }

    [Fact]
    public async Task Notify_SendsMailToAdmin()
    {
        using var notifier = CreateNotifier();
        await notifier.StartAsync(CancellationToken.None);

        notifier.Notify(Error("/api/matches/1/forecast"));
        await WaitForSendsAsync(1);
        await notifier.StopAsync(CancellationToken.None);

        _sender.Verify(s => s.SendAsync(
            It.Is<IEnumerable<string>>(to => to.Contains("admin@example.com")),
            It.Is<string>(subject => subject.Contains("HTTP 503")),
            It.Is<string>(body => body.Contains("AI servisi şu an yoğun.") && body.Contains("trace-1")),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notify_ThrottlesSameError_EvenOnDifferentPaths()
    {
        using var notifier = CreateNotifier();
        await notifier.StartAsync(CancellationToken.None);

        notifier.Notify(Error("/api/matches/1/forecast"));
        notifier.Notify(Error("/api/matches/2/forecast"));
        notifier.Notify(Error("/api/matches/3/forecast", "API-Football HTTP 500 döndü."));
        await WaitForSendsAsync(2);
        await notifier.StopAsync(CancellationToken.None);

        _sender.Verify(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Notify_DoesNothing_WhenDisabled()
    {
        using var notifier = CreateNotifier(enabled: false);
        await notifier.StartAsync(CancellationToken.None);

        notifier.Notify(Error("/api/matches/1/forecast"));
        await WaitForSendsAsync(1);
        await notifier.StopAsync(CancellationToken.None);

        _sender.VerifyNoOtherCalls();
    }
}