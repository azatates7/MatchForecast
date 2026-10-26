using System.Net.Http.Headers;
using System.Collections.Concurrent;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Email;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MatchForecast.Tests;

// Integration testleri için JWT ayarlarını sabitler, Redis'i ve veritabanını bellek içi sahteleriyle değiştirir.
internal static class TestAuth
{
    private const string SecretKey = "matchforecast-test-secret-key-32-bytes-min!";

    public const string UserName = "testuser";
    public const string Password = "Test-Password-123";

    // Testler user-secrets'a, çalışan bir Redis'e ve Supabase'e bağlı kalmadan ayağa kalkar.
    public static WebApplicationFactory<Program> WithTestJwt(this WebApplicationFactory<Program> factory)
    {
        var databaseName = Guid.NewGuid().ToString(); // Her host kendi boş veritabanını alır; testler birbirini etkilemez.

        return factory.WithWebHostBuilder(builder => builder
            /*
             * "Development" yerine "Testing": user-secrets yüklenmez, dolayısıyla Supabase bağlantısı boş kalır ve ayarlar
             * DB'den yüklenmez (testler gerçek veritabanına bağlanmaz). SMTP de kapatılır (şifre yok, ValidateOnStart).
             */
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:Enabled"] = "false"
            }))
            .ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtOptions>(o => o.SecretKey = SecretKey);
            services.AddSingleton<IRedisCacheService, InMemoryRedisCacheService>();

            /*
             * Testler geliştirici makinesindeki user-secrets'a bağlı kalmasın (CI agent'ında user-secrets yok):
             * - appsettings.json'da Ai:Provider=Ollama ve Ollama:BaseUrl boş; typed HttpClient "new Uri("")" ile patlar ve
             *   ForecastService'e bağlı MatchesController her istekte 500 döner. Testler AI'ı mock'lar, adres yalnızca geçerli olmalı.
             * - Tahmin sayısı testlerde sabitlenir (varsayılan 10).
             */
            services.PostConfigure<AiOptions>(o =>
            {
                if (string.IsNullOrWhiteSpace(o.Ollama.BaseUrl))
                    o.Ollama.BaseUrl = "http://localhost:11434/";
            });
            services.PostConfigure<ForecastOptions>(o =>
            {
                o.PredictionCount = 5;
                // Girişte arka plan ısıtması mock'lardaki AI çağrı sayılarını bozmasın; ForecastPrewarmTests ayrıca test eder.
                o.PrewarmOnLogin = false;
            });

            // Testler gerçek e-posta göndermez ve Google'a gitmez.
            services.AddSingleton<IEmailSender, FakeEmailSender>();
            services.AddSingleton<GoogleIdTokenValidator, FakeGoogleIdTokenValidator>();

            // Npgsql kaydı kaldırılıp bellek içi sağlayıcı eklenir (EF 9+ ayarları IDbContextOptionsConfiguration olarak biriktirir).
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName));
        }));
    }

    // Şifresi bilinen bir kullanıcı ekler (login testleri için).
    public static async Task SeedUserAsync(this WebApplicationFactory<Program> factory, bool isActive = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var user = new User { UserName = UserName, Email = $"{UserName}@test.local", IsActive = isActive };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    // Authorization: Bearer header'ı hazır HttpClient döner (veritabanına gitmeden, doğrudan imzalı token ile).
    public static HttpClient CreateAuthorizedClient(this WebApplicationFactory<Program> factory, params string[] roles)
    {
        var testFactory = factory.WithTestJwt();
        var client = testFactory.CreateClient();
        var token = testFactory.Services.GetRequiredService<JwtTokenService>().CreateAccessToken(1, "test", roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }
}

// Gönderilen e-postaları bellekte toplar (ör. şifre sıfırlama bağlantısını testte okumak için).
internal sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml, CancellationToken ct)
    {
        foreach (var address in to)
            Sent.Enqueue((address, subject, body));
        return Task.CompletedTask;
    }

    // Şifre sıfırlama e-postası arka planda gönderildiği için kısa bir süre beklenir.
    public async Task<string> WaitForBodyAsync(string to)
    {
        for (var i = 0; i < 50; i++)
        {
            var mail = Sent.FirstOrDefault(m => m.To == to);
            if (mail.Body is not null)
                return mail.Body;
            await Task.Delay(100);
        }
        throw new TimeoutException($"{to} adresine e-posta gönderilmedi.");
    }
}

// Yalnızca ValidToken için doğrulanmış bir Google kullanıcısı döner.
internal sealed class FakeGoogleIdTokenValidator() : GoogleIdTokenValidator(Options.Create(new AuthOptions()))
{
    public const string ValidToken = "valid-google-token";
    public static readonly GoogleUser User = new("google-sub-123", "google.user@gmail.com", true, "Google User");

    public override Task<GoogleUser?> ValidateAsync(string idToken, CancellationToken ct) =>
        Task.FromResult(idToken == ValidToken ? User : null);
}