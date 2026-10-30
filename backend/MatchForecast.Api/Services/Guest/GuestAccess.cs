using System.Security.Claims;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.Security.Guest;

/*
 * Misafir (Guest rolü) kısıtları. Frontend'deki gizleme yalnızca görünüm içindir; asıl kontrol burada, API tarafında yapılır.
 * - Yalnızca bugünün maçları (tarih parametresi yok sayılır).
 * - Yalnızca Türkiye / Süper Lig (API-Football'daki ülke ve lig adı).
 * - Cache temizleyemez, toplu AI tahmini (popular/forecasts) çalıştıramaz.
 */
public static class GuestAccess
{
    public const string Country = "Turkey";
    public const string League = "Süper Lig";

    // Misafire kapalı uçlar için: [Authorize(Policy = GuestAccess.NotGuestPolicy)]. Rolü olmayan kullanıcılar etkilenmez.
    public const string NotGuestPolicy = "NotGuest";

    public static bool IsGuest(ClaimsPrincipal user) => user.IsInRole(RoleNames.Guest);

    // "Bugün" sunucunun saat diliminden bağımsız olarak İstanbul saatine göre hesaplanır (fikstürler de bu saat diliminde gelir).
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).DateTime);

    public static bool IsAllowed(MatchSummary match) =>
        string.Equals(match.Country, Country, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(match.League, League, StringComparison.OrdinalIgnoreCase);

    // Maçın günü de İstanbul saatine çevrilerek bulunur; Kickoff hangi offset ile gelirse gelsin sonuç aynı olur.
    public static bool IsAllowedToday(MatchSummary match) =>
        IsAllowed(match) && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(match.Kickoff, Istanbul).DateTime) == Today;

    public static IReadOnlyList<MatchSummary> Filter(IReadOnlyList<MatchSummary> matches) =>
        matches.Where(IsAllowed).ToList();
}