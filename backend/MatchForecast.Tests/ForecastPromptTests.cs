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

    [Fact]
    public void BuildUser_WritesFormLines_WithVenue_AndOnlyKnownFields()
    {
        var form = new[]
        {
            new TeamForm("Galatasaray", "WWDLW", 2, 8, 2.3, 0.8, 2.1, 0.9),
            new TeamForm("Fenerbahce", null, 3, null, 1.6, null, null, null)
        };
        var prompt = ForecastPrompt.BuildUser(Match, new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now, null, form));

        Assert.Contains("Form (bu sezon, güncel web araması):", Lines(prompt));
        Assert.Contains("- Galatasaray: son 5 maç (en yeni önce) G G B M G; ligde 2. sıra; iç sahada maç başına 2,3 gol attı, 0,8 yedi (8 maç); xG 2,1 / 0,9 (lehine / aleyhine).", Lines(prompt));
        Assert.Contains("- Fenerbahce: ligde 3. sıra; deplasmanda maç başına 1,6 gol attı.", Lines(prompt));
    }

    [Fact]
    public void BuildUser_OmitsFormSection_WhenNotFound()
    {
        Assert.DoesNotContain("Form (", ForecastPrompt.BuildUser(Match, new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now)));
        Assert.DoesNotContain("Form (", ForecastPrompt.BuildUser(Match, new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now, null, [])));
    }

    [Fact]
    public void BuildUser_WritesRestDays_ComputedInCode()
    {
        var day = DateOnly.FromDateTime(Match.Kickoff.ToLocalTime().DateTime);
        var form = new[]
        {
            new TeamForm("Galatasaray", null, null, null, null, null, null, null, day.AddDays(-3), "UEFA Avrupa Ligi"),
            new TeamForm("Fenerbahce", "WWDLW", null, null, null, null, null, null, day.AddDays(-7))
        };
        var lines = Lines(ForecastPrompt.BuildUser(Match, new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now, null, form)));

        Assert.Contains("Dinlenme (son resmi maçtan bu maça): Galatasaray 3 gün (önceki maç: UEFA Avrupa Ligi); Fenerbahce 7 gün.", lines);
        // Yalnızca dinlenme bilgisi olan takım form bölümüne girmez (eksik oyuncular bölümünde adı geçmesi normal).
        var formSection = lines
            .SkipWhile(l => l != "Form (bu sezon, güncel web araması):")
            .Skip(1)
            .TakeWhile(l => l.Length > 0)
            .ToList();
        Assert.Equal(new[] { "- Fenerbahce: son 5 maç (en yeni önce) G G B M G." }, formSection);
    }

    [Fact]
    public void BuildUser_OmitsRestLine_WhenNoDates()
    {
        var form = new[] { new TeamForm("Galatasaray", "WWDLW", 2, null, null, null, null, null) };
        var prompt = ForecastPrompt.BuildUser(Match, new AbsenceReport(AbsenceReport.Ready, [], [], DateTimeOffset.Now, null, form));

        Assert.DoesNotContain("Dinlenme", prompt);
    }

    [Fact]
    public void BuildUser_MarksPreliminaryAbsences()
    {
        var report = new AbsenceReport(AbsenceReport.Preliminary,
            [new TeamAbsence("Galatasaray", "M. Icardi", TeamAbsence.Injured, new("Diz sakatlığı", "Knee"))], [], DateTimeOffset.Now);
        var lines = Lines(ForecastPrompt.BuildUser(Match, report));

        Assert.Contains("Eksik oyuncular (ön bilgi: şu ana kadar açıklananlar; maç günü kesinleşir):", lines);
        Assert.Contains("- Galatasaray: M. Icardi (sakat, Diz sakatlığı)", lines);
        Assert.Contains("- Fenerbahce: açıklanan eksik yok", lines);
    }
}