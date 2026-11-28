using System.Security.Claims;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Seed;

namespace MatchForecast.Api.Services.Security.Guest;

/*
 * Maç listesinde seçilebilecek tarihler. matches.any-date yetkisi olmayanlar (Admin örtük olarak sahip) yalnızca bugünün
 * ve yarının maçlarını görür; frontend tarih seçicisi de aynı aralıkla sınırlıdır, asıl kontrol burada (API) yapılır.
 * Misafir ayrıca yalnızca bugünü görür (GuestAccess). Tahmin ucu (GET /api/matches/{id}/forecast) bu kurala tabi değildir:
 * paylaşılan bir maç bağlantısı tarihinden bağımsız açılabilir.
 */
public static class MatchDateAccess
{
    public const int DaysAhead = 1;

    public static bool CanSelectAnyDate(ClaimsPrincipal user) => PermissionPolicy.HasPermission(user, PermissionKeys.MatchesAnyDate);

    public static bool IsAllowed(ClaimsPrincipal user, DateOnly date) =>
        CanSelectAnyDate(user) || (date >= GuestAccess.Today && date <= GuestAccess.Today.AddDays(DaysAhead));
}