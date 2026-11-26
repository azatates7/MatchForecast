using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

/*
 * Bir maç için eksik oyuncu raporu. Maç günü resmi açıklamalar (kulüp, lig/federasyon, UEFA, basın toplantısı)
 * web araması ile taranarak oluşturulur; elle girilmez.
 * State:
 *   - "Pending": maç günü gelmedi; eksikler maç günü netleşir (arama yapılmaz).
 *   - "Ready": maç günü tarandı; Players boş olabilir (resmi açıklamada eksik bulunamadı).
 *   - "Unavailable": özellik kapalı ya da arama başarısız; model ve ekran bu konuda varsayım yapmaz.
 */
public sealed record AbsenceReport(
    string State,
    IReadOnlyList<TeamAbsence> Players,
    IReadOnlyList<AbsenceSource> Sources,
    DateTimeOffset? CheckedAt)
{
    public const string Pending = "Pending";
    public const string Ready = "Ready";
    public const string Unavailable = "Unavailable";

    public static AbsenceReport PendingReport() => new(Pending, [], [], null);
    public static AbsenceReport UnavailableReport() => new(Unavailable, [], [], null);
}

/// <param name="Team">Fikstürdeki ev veya deplasman takımının adı (birebir).</param>
/// <param name="Status">"Suspended" (cezalı), "NotInSquad" (kadroda yok: UEFA listesi, kontenjan, teknik karar),
/// "Injured" (sakat, oynamayacak), "Doubtful" (şüpheli: yetişme ihtimali var / kesinleşmedi).</param>
public sealed record TeamAbsence(string Team, string Player, string Status, LocalizedText Reason)
{
    public const string Suspended = "Suspended";
    public const string NotInSquad = "NotInSquad";
    public const string Injured = "Injured";
    public const string Doubtful = "Doubtful";
}

/// <summary>Aramanın dayandığı web kaynağı (Gemini grounding). Url, Google'ın yönlendirme adresi olabilir.</summary>
public sealed record AbsenceSource(string Title, string Url);