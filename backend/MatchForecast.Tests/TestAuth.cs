using System.Net.Http.Headers;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

// Integration testleri için JWT ayarlarını sabitler, Redis'i bellek içi sahte servisle değiştirir ve token'lı HttpClient üretir.
internal static class TestAuth
{
    private const string SecretKey = "matchforecast-test-secret-key-32-bytes-min!";

    // Testler user-secrets'a ve çalışan bir Redis'e bağlı kalmadan ayağa kalkar; IConnectionMultiplexer hiç resolve edilmez.
    public static WebApplicationFactory<Program> WithTestJwt(this WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtOptions>(o => o.SecretKey = SecretKey);
            services.AddSingleton<IRedisCacheService, InMemoryRedisCacheService>();
        }));

    // Authorization: Bearer header'ı hazır HttpClient döner.
    public static HttpClient CreateAuthorizedClient(this WebApplicationFactory<Program> factory)
    {
        var testFactory = factory.WithTestJwt();
        var client = testFactory.CreateClient();
        var token = testFactory.Services.GetRequiredService<JwtTokenService>().CreateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}