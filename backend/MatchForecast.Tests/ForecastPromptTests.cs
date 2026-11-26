using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Response;

namespace MatchForecast.Tests;

// Eksik oyuncu bloğu: takım bazında listelenir; veri yoksa model bunu açıkça bilir (uydurmasın diye).
public class ForecastPromptTests
{
    private static readonly MatchSummary Match = new(1, DateTimeOffset.Now, "Süper Lig", "Turkey", "Galatasaray", "Fenerbahce", "NS");

    [Fact]
    public void BuildUser_ListsMissingPlayersPerTeam()
    {
        var prompt = ForecastPrompt.BuildUser(Match,
        [
            new("Galatasaray", "M. Icardi", "Missing Fixture", "Knee Injury"),
            new("Galatasaray", "L. Torreira", "Questionable", "Muscle Injury"),
            new("Fenerbahce", "F. Kadıoğlu", "Missing Fixture", "Suspended")
        ]);

        Assert.Contains("- Galatasaray: M. Icardi (oynamayacak, Knee Injury); L. Torreira (şüpheli, Muscle Injury)", prompt);
        Assert.Contains("- Fenerbahce: F. Kadıoğlu (oynamayacak, Suspended)", prompt);
    }

    [Fact]
    public void BuildUser_SaysNoReportedAbsences_ForEmptyList()
    {
        var prompt = ForecastPrompt.BuildUser(Match, []);

        Assert.Contains("- Galatasaray: bildirilen eksik yok", prompt);
        Assert.Contains("- Fenerbahce: bildirilen eksik yok", prompt);
    }

    [Fact]
    public void BuildUser_SaysUnavailable_WhenDataCouldNotBeLoaded()
    {
        var prompt = ForecastPrompt.BuildUser(Match, null);

        Assert.Contains("Eksik oyuncular: bilgi alınamadı.", prompt);
        Assert.DoesNotContain("bildirilen eksik yok", prompt);
    }
}