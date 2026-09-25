namespace MatchForecast.Models.Common;

/// <summary>Beklenen durumlar (maç yok, oran yok, dış servis hatası) için kullanıcıya gösterilebilir hata.</summary>
public sealed class ForecastException(string message, int statusCode = 502)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
