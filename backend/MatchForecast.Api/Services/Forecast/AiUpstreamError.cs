using MatchForecast.Models.Common;

namespace MatchForecast.Api.Services.Forecast;

/*
 * AI sağlayıcısı başarısız HTTP durum kodu döndürdüğünde kullanıcıya gösterilecek hata. Ham cevap ve anahtar bilgisi
 * mesaja konmaz (sağlayıcının iç hata metni, proje / kota ayrıntısı ekranda görünmesin); ayrıntı istemcide zaten loglanır.
 * UpstreamStatusCode, AI kullanım kaydına (ai_usages.status_code) sağlayıcının gerçek kodunu (ör. 429) yazmak içindir.
 */
public static class AiUpstreamError
{
    public static ForecastException Create(string provider, int status) => status switch
    {
        StatusCodes.Status429TooManyRequests => new ForecastException(
            Lang.T("AI servisinin istek kotası şu an dolu. Birkaç dakika sonra tekrar deneyin.",
                "The AI service's request quota is currently exhausted. Please try again in a few minutes."),
            StatusCodes.Status503ServiceUnavailable, status),
        StatusCodes.Status500InternalServerError or StatusCodes.Status502BadGateway
            or StatusCodes.Status503ServiceUnavailable or StatusCodes.Status504GatewayTimeout => new ForecastException(
            Lang.T("AI servisi şu an yoğun. Birkaç dakika sonra tekrar deneyin.",
                "The AI service is busy right now. Please try again in a few minutes."),
            StatusCodes.Status503ServiceUnavailable, status),
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => new ForecastException(
            Lang.T($"AI servisi ({provider}) isteği reddetti: anahtar veya yetki sorunu. Ayrıntı sunucu loglarında.",
                $"The AI service ({provider}) rejected the request: key or permission problem. See the server logs for details."),
            StatusCodes.Status502BadGateway, status),
        _ => new ForecastException(
            Lang.T($"AI servisi ({provider}) isteği işleyemedi (HTTP {status}). Ayrıntı sunucu loglarında.",
                $"The AI service ({provider}) could not process the request (HTTP {status}). See the server logs for details."),
            StatusCodes.Status502BadGateway, status)
    };
}