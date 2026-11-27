using MatchForecast.Api.Services.Forecast;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;

namespace MatchForecast.Tests;

// Eksik oyuncu bloğu: maç günü takım bazında listelenir; öncesinde/başarısızlıkta model bunu açıkça bilir (uydurmasın diye).
public class ForecastPromptTests
{
    private static readonly MatchSummary Match = new(1, DateTimeOffset.Now, "Süper Lig", "Turkey", "Galatasaray", "Fenerbahce", "NS");

    // Prompt satırları. Satır bazında tam (ordinal) eşleşme: kültüre duyarlı alt-metin aramasından etkilenmez, başarısız olursa xUnit tüm satırları yazdırır (hangi satırın farklı olduğu görünür).
    private static string[] Lines(string prompt) => prompt.Split('\n', StringSplitOptions.TrimEntries);

    [Fact]
    public void BuildUser_ListsAbsencesPerTeam_WithStatus()
    {
        var report = new AbsenceReport(AbsenceReport.Ready,
        [
            new TeamAbsence("Galatasaray", "M. Icardi", TeamAbsence.Injured, LocalizedText.Of("Diz sakatlığı", "Knee injury")),
            new TeamAbsence("Galatasaray", "L. Torreira", TeamAbsence.Doubtful, LocalizedText.Of("Kas ağrısı", "Muscle pain")),
            new TeamAbsence("Fenerbahce", "F. Kadıoğlu", TeamAbsence.Suspended, LocalizedText.Of("Sarı kart cezası", "Yellow card ban")),
            new TeamAbsence("Fenerbahce", "E. Džeko", TeamAbsence.NotInSquad, LocalizedText.Of("Kadro dışı", "Left out"))
        ], [], DateTimeOffset.Now);

        var prompt = ForecastPrompt.BuildUser(Match, report);

        Assert.Contains("- Galatasaray: M. Icardi (sakat, Diz sakatlığı); L. Torreira (şüpheli, Kas ağrısı)", Lines(prompt));
        Assert.Contains("- Fenerbahce: F. Kadıoğlu (cezalı, Sarı kart cezası); E. Džeko (kadroda yok, Kadro dışı)", Lines(prompt));
    }

    [Fact]
    public void BuildUser_SaysNoAnnouncedAbsences_ForEmptyReadyReport()
    {
        var prompt = ForecastPrompt.BuildUser(Match, new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now));

        Assert.Contains("- Galatasaray: açıklanan eksik yok", Lines(prompt));
        Assert.Contains("- Fenerbahce: açıklanan eksik yok", Lines(prompt));
    }

    [Fact]
    public void BuildUser_SaysPending_BeforeMatchDay()
    {
        var prompt = ForecastPrompt.BuildUser(Match, AbsenceReport.PendingReport());

        Assert.Contains("Eksik oyuncular: henüz netleşmedi (maç günü açıklanır).", Lines(prompt));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildUser_SaysUnavailable_WhenNoData(bool nullReport)
    {
        var prompt = ForecastPrompt.BuildUser(Match, nullReport ? null : AbsenceReport.UnavailableReport());

        Assert.Contains("Eksik oyuncular: bilgi alınamadı.", Lines(prompt));
        Assert.DoesNotContain(Lines(prompt), l => l.Contains("açıklanan eksik yok", StringComparison.Ordinal));
    }

    // ---------- Hakem ----------

    [Fact]
    public void BuildUser_AddsRefereeLine_WhenStatsWereFound()
    {
        var referee = new RefereeInfo("Halil Umut Meler", 12, 4.8, 0.2, 0.25, null,
            LocalizedText.Of("Sert müdahaleye hızlı kart gösterir.", "Quick to book hard tackles."), RefereeInfo.Balanced);
        var report = new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now, referee);

        var prompt = ForecastPrompt.BuildUser(Match, report);

        // Faul bilinmiyor → yazılmaz; tarzın sonundaki nokta çiftlenmez.
        Assert.Contains("Hakem: Halil Umut Meler — maç başına 4,8 sarı, 0,2 kırmızı, 0,25 penaltı (12 maç). Tarz: Sert müdahaleye hızlı kart gösterir.", Lines(prompt));
    }

    [Fact]
    public void BuildUser_OmitsReferee_WhenNotFoundOrOnlyNameKnown()
    {
        var nameOnly = new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now,
            new RefereeInfo("Halil Umut Meler", null, null, null, null, null, null, null));

        Assert.DoesNotContain("Hakem", ForecastPrompt.BuildUser(Match, nameOnly));
        Assert.DoesNotContain("Hakem", ForecastPrompt.BuildUser(Match, new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now)));
    }
}