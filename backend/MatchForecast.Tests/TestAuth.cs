using System.Net.Http.Headers;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

/// <summary>Integration testleri için JWT ayarlarını sabitler ve token'lı HttpClient üretir.</summary>
internal static class TestAuth
{
    private const string SecretKey = "matchforecast-test-secret-key-32-bytes-min!";

    /// <summary>User-secrets'a bağlı kalmadan testlerin geçerli bir JwtOptions ile ayağa kalkmasını sağlar.</summary>
    public static WebApplicationFactory<Program> WithTestJwt(this WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtOptions>(o => o.SecretKey = SecretKey)));

    /// <summary>Authorization: Bearer header'ı hazır HttpClient döner.</summary>
    public static HttpClient CreateAuthorizedClient(this WebApplicationFactory<Program> factory)
    {
        var testFactory = factory.WithTestJwt();
        var client = testFactory.CreateClient();
        var token = testFactory.Services.GetRequiredService<JwtTokenService>().CreateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}