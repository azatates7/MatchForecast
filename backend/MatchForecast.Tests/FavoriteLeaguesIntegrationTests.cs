using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MatchForecast.Tests;

// Favori ligler kullanıcıya özgüdür; ekleme/silme idempotenttir, misafire kapalıdır.
public class FavoriteLeaguesIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/me/favorite-leagues";
    private const string SuperLig = "Turkey / Süper Lig";
    private const string PremierLeague = "England / Premier League";

    private static string WithLeague(string league) => $"{Url}?league={Uri.EscapeDataString(league)}";

    private static HttpClient ClientFor(WebApplicationFactory<Program> testFactory, int userId, params string[] roles)
    {
        var client = testFactory.CreateClient();
        var token = testFactory.Services.GetRequiredService<JwtTokenService>().CreateAccessToken(userId, $"user{userId}", roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    [Fact]
    public async Task AddAndRemove_AreIdempotent_AndPerUser()
    {
        var testFactory = factory.WithTestJwt();
        var user1 = ClientFor(testFactory, 1);
        var user2 = ClientFor(testFactory, 2);

        Assert.Empty(await user1.GetFromJsonAsync<List<string>>(Url) ?? []);

        await user1.PutAsync(WithLeague(SuperLig), null);
        var afterAdd = await (await user1.PutAsync(WithLeague(SuperLig), null)).Content.ReadFromJsonAsync<List<string>>();
        Assert.Equal(SuperLig, Assert.Single(afterAdd!));

        await user2.PutAsync(WithLeague(PremierLeague), null);
        Assert.Equal(SuperLig, Assert.Single((await user1.GetFromJsonAsync<List<string>>(Url))!));
        Assert.Equal(PremierLeague, Assert.Single((await user2.GetFromJsonAsync<List<string>>(Url))!));

        await user1.DeleteAsync(WithLeague(SuperLig));
        var afterRemove = await (await user1.DeleteAsync(WithLeague(SuperLig))).Content.ReadFromJsonAsync<List<string>>();
        Assert.Empty(afterRemove ?? []);
        Assert.Equal(PremierLeague, Assert.Single((await user2.GetFromJsonAsync<List<string>>(Url))!));
    }

    [Fact]
    public async Task Add_EmptyLeague_ReturnsBadRequest()
    {
        var client = factory.CreateAuthorizedClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"{Url}?league=%20", null)).StatusCode);
    }

    [Fact]
    public async Task Guest_ReturnsForbidden()
    {
        var client = factory.CreateAuthorizedClient(RoleNames.Guest);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync(WithLeague(SuperLig), null)).StatusCode);
    }
}