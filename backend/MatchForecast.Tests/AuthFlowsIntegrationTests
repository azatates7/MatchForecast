using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MatchForecast.Api.Services.Email;
using MatchForecast.Data.Configuration;
using MatchForecast.Models.Request;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

// Kayıt, şifre sıfırlama ve Google ile giriş akışları (bellek içi DB, sahte e-posta ve sahte Google doğrulayıcı ile).
public class AuthFlowsIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Register_CreatesUser_AndReturnsTokens()
    {
        var client = factory.WithTestJwt().CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("NewUser", "new.user@test.local", "New-Password-123", "Yeni Kullanıcı"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(token!.AccessToken));

        // Kayıt sonrası aynı bilgilerle giriş yapılabilir (kullanıcı adı küçük harfe çevrilmiş olsa da).
        var login = await client.PostAsJsonAsync("/api/auth/token", new GetTokenRequest("newuser", "New-Password-123"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Register_ReturnsConflict_ForTakenUserName()
    {
        var testFactory = factory.WithTestJwt();
        await testFactory.SeedUserAsync();

        var response = await testFactory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(TestAuth.UserName, "other@test.local", "New-Password-123", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ReturnsNoContent_ForUnknownEmail_WithoutSendingMail()
    {
        var testFactory = factory.WithTestJwt();
        var client = testFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("nobody@test.local"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(((FakeEmailSender)testFactory.Services.GetRequiredService<IEmailSender>()).Sent);
    }

    [Fact]
    public async Task ResetPassword_ChangesPassword_AndLinkIsSingleUse()
    {
        var testFactory = factory.WithTestJwt();
        await testFactory.SeedUserAsync();
        var client = testFactory.CreateClient();
        var email = $"{TestAuth.UserName}@test.local";

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email))).StatusCode);

        var mailBody = await ((FakeEmailSender)testFactory.Services.GetRequiredService<IEmailSender>()).WaitForBodyAsync(email);
        var resetToken = Uri.UnescapeDataString(Regex.Match(mailBody, "resetToken=([^\"&]+)").Groups[1].Value);

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(resetToken, "Brand-New-Pass-1"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/token", new GetTokenRequest(TestAuth.UserName, TestAuth.Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/token", new GetTokenRequest(TestAuth.UserName, "Brand-New-Pass-1"))).StatusCode);

        var reuse = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(resetToken, "Another-Pass-123"));
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task GoogleLogin_CreatesUserOnce_AndLinksAccount()
    {
        var testFactory = factory.WithTestJwt();
        var client = testFactory.CreateClient();
        var request = new GoogleLoginRequest(FakeGoogleIdTokenValidator.ValidToken);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/google", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/google", request)).StatusCode);

        using var scope = testFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.Include(x => x.Logins).SingleAsync();
        Assert.Equal(FakeGoogleIdTokenValidator.User.Email, user.Email);
        Assert.True(user.EmailConfirmed);
        Assert.Single(user.Logins);
    }

    [Fact]
    public async Task GoogleLogin_ReturnsUnauthorized_ForInvalidToken()
    {
        var client = factory.WithTestJwt().CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/google", new GoogleLoginRequest("forged-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}