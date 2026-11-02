using System.Net;
using System.Net.Http.Json;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatchForecast.Tests;

/*
 * i18n: hata mesajları Accept-Language'a göre (tr varsayılan, en), tahmin metinleri ise her zaman { tr, en } olarak döner.
 */
public class LocalizationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(null, "Şifre uzunluğu")]
    [InlineData("tr-TR", "Şifre uzunluğu")]
    [InlineData("en", "Password length")]
    [InlineData("en-US,en;q=0.9", "Password length")]
    [InlineData("de-DE", "Şifre uzunluğu")] // Desteklenmeyen dil -> varsayılan (tr)
    public async Task ProblemDetail_FollowsAcceptLanguage(string? acceptLanguage, string expected)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/password?length=3");
        if (acceptLanguage is not null)
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.StartsWith(expected, problem!.Detail);
    }

    [Fact]
    public void Projection_KeepsBothLanguages_AndFallsBackToTurkish()
    {
        var ai = new AiStatsResponse("Dengeli maç.", "Balanced match.",
            new AiMetric(1.6, "Ev sahibi içeride etkili.", "Strong at home."),
            new AiMetric(1.0, "Deplasman hücumu zayıf.", "Weak away attack."),
            new AiMetric(1.1, "İlk yarı temkinli."), // İngilizce yok -> Türkçesi kullanılır
            null, null, null);

        var projection = ProjectionBuilder.Build(ai, new ForecastOptions(), NullLogger.Instance);
        var picks = PredictionSelector.Select(projection, new ForecastOptions { PredictionCount = 10 });

        var total = projection.Stats.Single(s => s.Metric == StatMetric.TotalGoals);
        Assert.Equal(new LocalizedText("Toplam gol", "Total goals"), total.Label);
        Assert.Equal("Strong at home. Weak away attack.", total.Reasoning.En);

        Assert.Equal("İlk yarı temkinli.", projection.Reasons[StatMetric.FirstHalfGoals].En);

        var homeOver = picks.Single(p => p.Label.Tr == "Ev 0.5 Üst");
        Assert.Equal("Home Over 0.5", homeOver.Label.En);
        Assert.Equal("Strong at home.", homeOver.Reasoning.En);
    }

    [Fact]
    public void Catalog_HasEnglishLabel_ForEveryOption()
    {
        Assert.All(ForecastCatalog.Options, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Label.Tr));
            Assert.False(string.IsNullOrWhiteSpace(o.Label.En));
            Assert.NotEqual(o.Label.Tr, o.Label.En);
        });
    }
}
