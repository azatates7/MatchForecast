using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Email;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MatchForecast.Tests;

public class StartupNotificationServiceTests
{
    private readonly Mock<IEmailSender> _sender = new();
    private readonly CancellationTokenSource _started = new();

    private StartupNotificationService CreateService(bool enabled = true, bool sendStartup = true)
    {
        var smtp = Options.Create(new SmtpOptions { Enabled = enabled, SendStartupNotification = sendStartup, AdminEmails = ["admin@example.com"] });
        var lifetime = Mock.Of<IHostApplicationLifetime>(l => l.ApplicationStarted == _started.Token);
        var env = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Test");
        var server = Mock.Of<IServer>(s => s.Features == new FeatureCollection());
        return new StartupNotificationService(_sender.Object, smtp, Options.Create(new AiOptions()), lifetime, env, server,
            NullLogger<StartupNotificationService>.Instance);
    }

    private async Task WaitForSendAsync()
    {
        for (var i = 0; i < 50 && _sender.Invocations.Count == 0; i++)
            await Task.Delay(20);
    }

    [Fact]
    public async Task SendsMail_OnlyAfterApplicationStarted()
    {
        using var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        // ApplicationStarted tetiklenmeden mail gitmemeli (Kestrel henüz hazır değil).
        await Task.Delay(100);
        _sender.VerifyNoOtherCalls();

        _started.Cancel();
        await WaitForSendAsync();
        await service.StopAsync(CancellationToken.None);

        _sender.Verify(s => s.SendAsync(
            It.Is<IEnumerable<string>>(to => to.Contains("admin@example.com")),
            It.Is<string>(subject => subject.Contains("Uygulama başlatıldı")),
            It.IsAny<string>(),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DoesNotSend_WhenSmtpOrStartupNotificationDisabled(bool enabled, bool sendStartup)
    {
        using var service = CreateService(enabled, sendStartup);
        await service.StartAsync(CancellationToken.None);
        _started.Cancel();
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _sender.VerifyNoOtherCalls();
    }
}