namespace MatchForecast.Models.Common;

/// <summary>Beklenen durumlar (maç yok, oran yok, dış servis hatası) için kullanıcıya gösterilebilir hata.</summary>
/// <param name="statusCode">İstemciye dönen HTTP kodu.</param>
/// <param name="upstreamStatusCode">Dış servisin (AI sağlayıcısı) döndürdüğü kod, ör. 429; kullanım kaydı için. Yoksa null.</param>
public sealed class ForecastException(string message, int statusCode = 502, int? upstreamStatusCode = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int? UpstreamStatusCode { get; } = upstreamStatusCode;
}