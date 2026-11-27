using System.Net;
using System.Net.Sockets;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.Absences;

public interface ISourceLinkChecker
{
    /// <summary>Açılamayan (404/410, ağ hatası) veya güvensiz adresleri eler; sırayı korur. Hiçbir zaman exception fırlatmaz.</summary>
    Task<IReadOnlyList<AbsenceSource>> FilterReachableAsync(IReadOnlyList<AbsenceSource> sources, CancellationToken ct);
}

/*
 * Gemini bazı cevaplarda arama kaynaklarını (groundingMetadata.groundingChunks) döndürmüyor; bu durumda modelin
 * cevabına yazdığı adresler kullanılır. Model adres uydurabileceği için her adres gösterilmeden önce açılıp denetlenir:
 * - Yalnızca http/https ve varsayılan port; 404/410 ve bağlantı hatası elenir. 403/429 gibi cevaplar kalır
 *   (haber siteleri botları engelleyebilir; sayfa var demektir).
 * - SSRF koruması: adres yerel/özel bir IP'ye çözülürse (127.0.0.1, 10.x, 169.254.169.254 AWS metadata vb.) bağlanılmaz.
 *   Kontrol bağlantı anında yapıldığı için DNS'in sonradan değişmesi (rebinding) de engellenir. Yönlendirme izlenmez.
 * - Gövde okunmaz (yalnızca başlıklar), her adres 5 sn ile sınırlıdır, adresler paralel denetlenir.
 * Bu HttpClient Gemini anahtarını taşımaz; ayrı kayıtlıdır (Program.cs).
 */
public sealed class SourceLinkChecker(HttpClient http, ILogger<SourceLinkChecker> logger) : ISourceLinkChecker
{
    public async Task<IReadOnlyList<AbsenceSource>> FilterReachableAsync(IReadOnlyList<AbsenceSource> sources, CancellationToken ct)
    {
        var checks = sources.Select(async s => (Source: s, Ok: await IsReachableAsync(s.Url, ct)));
        var results = await Task.WhenAll(checks);
        return results.Where(r => r.Ok).Select(r => r.Source).ToList();
    }

    private async Task<bool> IsReachableAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Absence source {Url} is not reachable; it is not shown.", url);
            return false;
        }
    }

    // SocketsHttpHandler.ConnectCallback: adres bağlanmadan önce çözülür; herhangi biri yerel/özel ise bağlantı reddedilir.
    public static async ValueTask<Stream> ConnectPublicOnlyAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || !addresses.All(IsPublic))
            throw new HttpRequestException($"Host {context.DnsEndPoint.Host} resolves to a non-public address.");

        var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0                                     // 0.0.0.0/8
                     || b[0] == 10                                  // 10/8
                     || b[0] == 127                                 // loopback
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64/10 (CGNAT)
                     || (b[0] == 169 && b[1] == 254)                // link-local, AWS metadata
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16/12
                     || (b[0] == 192 && b[1] == 168)                // 192.168/16
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0/24
                     || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // 198.18/15
                     || b[0] >= 224);                               // multicast ve ayrılmış
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return !(IPAddress.IPv6Loopback.Equals(ip)
                     || IPAddress.IPv6None.Equals(ip)
                     || ip.IsIPv6LinkLocal
                     || ip.IsIPv6SiteLocal
                     || ip.IsIPv6Multicast
                     || (b[0] & 0xFE) == 0xFC);                     // fc00::/7 (unique local)
        }

        return false;
    }
}