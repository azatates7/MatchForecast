namespace MatchForecast.Models.Common;

/// <summary>API-Football "fixture.status.short" kodlarının gruplanması.</summary>
public static class MatchStatus
{
    /// <summary>Oynanması bitmiş ya da artık oynanmayacak maçlar: normal süre, uzatma, penaltı, hükmen, iptal, yarıda kalan.</summary>
    private static readonly HashSet<string> Finished = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "AET", "PEN", "AWD", "WO", "CANC", "ABD"
    };

    /// <summary>Devam eden maçlar: 1. yarı, devre arası, 2. yarı, uzatma, uzatma arası, penaltılar, askıda, kesinti.</summary>
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "1H", "HT", "2H", "ET", "BT", "P", "SUSP", "INT", "LIVE"
    };

    public static bool IsFinished(string? status) => status is not null && Finished.Contains(status);

    public static bool IsLive(string? status) => status is not null && Live.Contains(status);
}