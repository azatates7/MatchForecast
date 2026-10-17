using System.Net;
using System.Net.Http.Json;
using MatchForecast.Api.Services.Security;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MatchForecast.Tests;

public class PasswordApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PasswordApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(128)]
    public void Generate_ReturnsRequestedLength_WithEveryCharacterGroup(int length)
    {
        var password = PasswordGenerator.Generate(length);

        Assert.Equal(length, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void Generate_ProducesDistinctPasswords()
    {
        var passwords = Enumerable.Range(0, 1000).Select(_ => PasswordGenerator.Generate()).ToHashSet();

        Assert.Equal(1000, passwords.Count);
    }

    [Fact]
    public async Task Endpoint_ReturnsPassword_WithToken()
    {
        var client = _factory.CreateAuthorizedClient();

        var response = await client.GetAsync("/api/password?length=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PasswordResponse>();
        Assert.NotNull(body);
        Assert.Equal(20, body.Password.Length);
        Assert.Equal(20, body.Length);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(129)]
    public async Task Endpoint_ReturnsBadRequest_ForInvalidLength(int length)
    {
        var client = _factory.CreateAuthorizedClient();

        var response = await client.GetAsync($"/api/password?length={length}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_ReturnsUnauthorized_WithoutToken()
    {
        var client = _factory.WithTestJwt().CreateClient();

        var response = await client.GetAsync("/api/password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}