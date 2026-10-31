using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

// TokenCleanupWorker.CleanupAsync: hangi kayıtların silindiği, hangilerinin bilerek tutulduğu.
public class TokenCleanUpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Cleanup_RemovesExpiredAndUsedTokens_KeepsRevokedButUnexpiredRefreshTokens()
    {
        var testFactory = factory.WithTestJwt();
        await testFactory.SeedUserAsync();
        using var scope = testFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = (await db.Users.SingleAsync()).Id;
        var now = DateTime.Now;

        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = userId, TokenHash = "expired", ExpiresAt = now.AddMinutes(-1) },
            // İptal edilmiş ama süresi dolmamış: reuse detection için tutulur.
            new RefreshToken { UserId = userId, TokenHash = "revoked", ExpiresAt = now.AddDays(1), RevokedAt = now.AddHours(-1) },
            new RefreshToken { UserId = userId, TokenHash = "active", ExpiresAt = now.AddDays(1) });
        db.PasswordResetTokens.AddRange(
            new PasswordResetToken { UserId = userId, TokenHash = "used", ExpiresAt = now.AddMinutes(30), UsedAt = now.AddMinutes(-5) },
            new PasswordResetToken { UserId = userId, TokenHash = "expired", ExpiresAt = now.AddMinutes(-1) },
            new PasswordResetToken { UserId = userId, TokenHash = "pending", ExpiresAt = now.AddMinutes(30) });
        await db.SaveChangesAsync();

        var (refreshTokens, resetTokens) = await TokenCleanUpWorker.CleanupAsync(db, now, CancellationToken.None);

        Assert.Equal(1, refreshTokens);
        Assert.Equal(2, resetTokens);
        Assert.Equal(new[] { "active", "revoked" }, await db.RefreshTokens.Select(x => x.TokenHash).OrderBy(x => x).ToListAsync());
        Assert.Equal("pending", Assert.Single(await db.PasswordResetTokens.ToListAsync()).TokenHash);
    }
}