using System.Net.Http.Json;
using System.Text.Json;
using MatchForecast.Models.Common;
using MatchForecast.Api.Options;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.AI.Ollama;

/*
 * Ollama Chat API (/api/chat) istemcisi; Ai:Provider "Ollama" olduğunda kullanılır.
 * Gemini/Claude istemcileriyle aynı sözleşmeyi (IForecastAiClient) uygular, bu yüzden ForecastService,
 * prompt'lar ve JSON ayrıştırma hiç değişmeden çalışır.
 */
public sealed class OllamaForecastClient(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<OllamaForecastClient> logger) : IForecastAiClient
{
    private readonly OllamaOptions _opt = options.Value.Ollama;
    private readonly int _maxTokens = options.Value.MaxTokens;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        /*
         * stream=false: cevap tek JSON nesnesi olarak gelir (varsayılan true'dur ve satır satır parça döner).
         * format="json": Ollama çıktıyı geçerli JSON'a zorlar; küçük modellerin markdown/ön metin eklemesini engeller.
         * options.num_predict: üretilecek en fazla token (Ai:MaxTokens), options.num_ctx: bağlam penceresi.
         */
        var body = new
        {
            model = _opt.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            format = "json",
            options = new { num_predict = _maxTokens, num_ctx = _opt.ContextLength }
        };

        HttpResponseMessage res;
        try
        {
            res = await http.PostAsJsonAsync("api/chat", body, ct);
        }
        catch (HttpRequestException ex)
        {
            // Bulut API'lerinin aksine en sık hata "Ollama çalışmıyor"; genel 500 yerine anlaşılır mesaj dönülür.
            logger.LogError(ex, "Ollama server is unreachable at {BaseUrl}.", _opt.BaseUrl);
            throw new ForecastException($"Ollama sunucusuna ulaşılamadı ({_opt.BaseUrl}). 'ollama serve' çalışıyor mu?",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // İstemci iptal etmediyse bu HttpClient.Timeout'tur (model yavaş veya ilk yükleniyor).
            logger.LogError(ex, "Ollama request timed out after {Timeout}s. Model: {Model}", _opt.TimeoutSeconds, _opt.Model);
            throw new ForecastException($"Ollama {_opt.TimeoutSeconds} sn içinde yanıt vermedi. Ai:Ollama:TimeoutSeconds değerini artırın veya daha küçük bir model kullanın.",
                StatusCodes.Status504GatewayTimeout);
        }

        using (res)
        {
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                // Ollama hataları {"error":"..."} formatında döner; 404 genelde modelin indirilmediği anlamına gelir.
                logger.LogError("Ollama API call failed with status code {StatusCode}. Model: {Model}. Raw response: {Raw}", (int)res.StatusCode, _opt.Model, raw);
                throw res.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? new ForecastException($"Ollama modeli bulunamadı: '{_opt.Model}'. 'ollama pull {_opt.Model}' ile indirin.")
                    : new ForecastException($"AI servisi (Ollama) HTTP {(int)res.StatusCode}: {Truncate(raw, 300)}");
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Düşünen (thinking) modellerde akıl yürütme "message.thinking" alanına ayrılır; biz yalnızca "content"i alırız.
            var text = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
                ? content.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(text))
            {
                var reason = root.TryGetProperty("done_reason", out var dr) ? dr.GetString() : "unknown";
                logger.LogError("Ollama returned empty content. DoneReason: {Reason}. Raw response: {Raw}", reason, raw);
                throw new ForecastException(reason == "length"
                    ? "AI yanıtı token limitine takıldı. Ai:MaxTokens değerini artırın."
                    : $"AI servisi (Ollama) boş yanıt döndü (done_reason: {reason}).");
            }

            return text;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}