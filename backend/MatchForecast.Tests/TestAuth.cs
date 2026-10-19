using System.Net.Http.Headers;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtOptions>(o => o.SecretKey = SecretKey);
            services.AddSingleton<IRedisCacheService, InMemoryRedisCacheService>();

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