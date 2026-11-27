using MatchForecast.Models.Common;

namespace MatchForecast.Models.Response;

/*
 * Bir maç için eksik oyuncu raporu. Maç günü resmi açıklamalar (kulüp, lig/federasyon, UEFA, basın toplantısı)
 * web araması ile taranarak oluşturulur; elle girilmez.
 * State:
 *   - "Pending": maç günü gelmedi; eksikler maç günü netleşir (arama yapılmaz).
 *   - "Ready": maç günü tarandı; Players boş olabilir (resmi açıklamada eksik bulunamadı).
 *   - "Unavailable": özellik kapalı ya da arama başarısız; model ve ekran bu konuda varsayım yapmaz.
 * Referee: aynı maç günü aramasında bulunan hakem bilgisi (ek arama yapılmaz). Bulunamadıysa null; o durumda ekranda hakem bölümü ve prompt'ta hakem satırı olmaz. Sources her ikisinin kaynaklarını içerir.
 * Form: aynı aramada bulunan takımların güncel form verisi (ek arama yapılmaz). Bulunamayan takım listede yer almaz; liste boşsa prompt'ta form satırı olmaz.
 */
public sealed record AbsenceReport(
    string State,
    IReadOnlyList<TeamAbsence> Players,
    IReadOnlyList<AbsenceSource> Sources,
    DateTimeOffset? CheckedAt,
    RefereeInfo? Referee = null,
    IReadOnlyList<TeamForm>? Form = null)
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

/*
 * Maçın hakemi ve kart eğilimi. Name dışındaki her alan bulunamadıysa null'dır (uydurulmaz, ekranda gösterilmez).
 * Ortalamalar maç başınadır; Matches bunların dayandığı maç sayısı (tercihen bu sezon, tüm organizasyonlar).
 * Strictness kodda sarı kart ortalamasından hesaplanır (modelin yorumuna bırakılmaz):
 *   "Strict" (>= 5.0 sarı), "Lenient" (< 3.5 sarı), arası "Balanced"; ortalama yoksa null.
 */
public sealed record RefereeInfo(
    string Name,
    int? Matches,
    double? YellowPerMatch,
    double? RedPerMatch,
    double? PenaltiesPerMatch,
    double? FoulsPerMatch,
    LocalizedText? Style,
    string? Strictness)
{
    public const string Strict = "Strict";
    public const string Balanced = "Balanced";
    public const string Lenient = "Lenient";

    public static string? StrictnessFor(double? yellowPerMatch) => yellowPerMatch switch
    {
        null => null,
        >= 5.0 => Strict,
        < 3.5 => Lenient,
        _ => Balanced
    };
}

/*
 * Takımın bu sezonki güncel formu (maç günü araması). Team dışındaki her alan bulunamadıysa null'dır (uydurulmaz).
 * - Last5: son 5 resmi maç, en yeni önce, W/D/L harfleriyle (ör. "WWDLW").
 * - Venue*: bu maçtaki sahaya göre; ev sahibi için iç saha, deplasman takımı için deplasman maçları.
 *   VenueMatches bu ortalamaların dayandığı maç sayısı. Xg* aynı maçların beklenen gol (xG) ortalamaları.
 */
public sealed record TeamForm(
    string Team,
    string? Last5,
    int? LeaguePosition,
    int? VenueMatches,
    double? VenueGoalsFor,
    double? VenueGoalsAgainst,
    double? XgFor,
    double? XgAgainst)
{
    public bool HasData => Last5 is not null || LeaguePosition is not null || VenueGoalsFor is not null
                           || VenueGoalsAgainst is not null || XgFor is not null || XgAgainst is not null;
}

/// <summary>Aramanın dayandığı web kaynağı (Gemini grounding). Url, Google'ın yönlendirme adresi olabilir.</summary>
public sealed record AbsenceSource(string Title, string Url);