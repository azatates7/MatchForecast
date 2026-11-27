using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Models.Common;
using MatchForecast.Models.Response;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Absences;

public interface IAbsenceResearcher
{
    /// <summary>Maçın eksik oyuncu raporu. Maç günü değilse "Pending"; hata/kapalıysa "Unavailable". Hiçbir zaman exception fırlatmaz.</summary>
    Task<AbsenceReport> GetAsync(MatchSummary match, CancellationToken ct);
}

/*
 * Maç günü, Gemini'nin Google araması (tools: google_search) ile iki takımın eksik oyuncularını çıkarır.
 * - Maç gününden önce arama yapılmaz ("Pending"): kadro, ceza ve UEFA listesi bilgisi maç gününe doğru netleşir.
 * - Kaynakta açıkça yazmayan oyuncu eklenmez; kesinleşmeyen her durum "Doubtful" (şüpheli) olarak yazılır.
 * - Sonuç Absences:CacheMinutes kadar önbellekte kalır; başarısız deneme 15 dk önbelleğe alınır (ücretli aramayı tekrarlamamak için).
 * - Fail-open: kapalı, anahtar yok, ücretsiz katman (400), kota veya geçersiz cevap → "Unavailable"; tahmin akışı durmaz.
 * - Her gerçek arama (önbellekten gelmeyen) AI kullanım raporuna (ai_usages, Purpose=Absences) token ve sorgu sayısıyla yazılır.
 * - Absences:MonthlySearchLimit: bu ayki sorgu sayısı sınıra ulaştıysa arama yapılmaz ("Unavailable"), ay başında açılır.
 * - Kaynaklar: Google'ın döndürdüğü arama kaynakları (groundingChunks); Gemini bazen bunları göndermediği için yoksa modelin cevabına yazdığı adresler açılabilir olduğu denetlenerek kullanılır (ISourceLinkChecker).
 */
public sealed class GeminiAbsenceResearcher(
    HttpClient http,
    IRedisCacheService cache,
    IOptions<AiOptions> aiOptions,
    IOptions<AbsencesOptions> absenceOptions,
    IAiUsageStore usageStore,
    ISourceLinkChecker linkChecker,
    ILogger<GeminiAbsenceResearcher> logger) : IAbsenceResearcher
{
    private const int MaxPlayersPerTeam = 20;
    private const int MaxSources = 5;
    private static readonly TimeSpan FailureCacheTtl = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public const string SystemPrompt = """
        Sen bir futbol veri asistanısın. Google araması yaparak verilen maçta iki takımın OYNAYAMAYACAK veya
        oynaması ŞÜPHELİ oyuncularını bul.

        Kaynaklar: son 7 gündeki resmi açıklamalar (kulüp, lig/federasyon, UEFA), teknik direktörün maç öncesi
        basın toplantısı ve güvenilir spor haberleri. Eski haberleri kullanma.

        Durumlar:
        - Suspended: cezalı (kırmızı kart, sarı kart birikimi, disiplin cezası).
        - NotInSquad: kadroda yok (UEFA/turnuva listesine yazılmamış, kontenjan dışı, teknik karar, kamp kadrosuna alınmamış).
          UEFA listesi gibi turnuvaya özel durumları yalnızca bu maçın turnuvası için geçerliyse yaz.
        - Injured: sakat veya hasta ve bu maçta oynamayacağı açıkça belirtilmiş.
        - Doubtful: yetişme ihtimali var, durumu maç günü netleşecek ya da kaynaklar çelişiyor.

        Kurallar:
        - Kesin olmayan her durumu Doubtful yaz.
        - Kaynakta açıkça geçmeyen hiçbir oyuncuyu ekleme, isim uydurma. Bulamazsan boş liste döndür.
        - team alanına SADECE kullanıcı mesajındaki iki takım adından birini birebir yaz.
        - reason: kısa Türkçe neden (ör. "Diz sakatlığı", "Sarı kart cezası", "UEFA listesinde yok").
          reasonEn: aynı nedenin İngilizcesi.
        - sources: bilgiyi aldığın sayfaların tam adresleri (en fazla 5; kulüp, federasyon, UEFA veya haber sitesi).
          Yalnızca aramada gerçekten gördüğün adresleri yaz; adres uydurma. title: sitenin adı.
        - Cevabın SADECE geçerli JSON olsun; açıklama, markdown veya kod bloğu ekleme.

        JSON şeması:
        { "players": [ { "team": "...", "player": "...", "status": "Suspended|NotInSquad|Injured|Doubtful", "reason": "...", "reasonEn": "..." } ],
          "sources": [ { "title": "...", "url": "https://..." } ] }
        """;

    public async Task<AbsenceReport> GetAsync(MatchSummary match, CancellationToken ct)
    {
        var opt = absenceOptions.Value;
        var gemini = aiOptions.Value.Gemini;
        if (!opt.Enabled || string.IsNullOrWhiteSpace(gemini.ApiKey))
            return AbsenceReport.UnavailableReport();

        // Konteyner saati İstanbul (TZ); maç günü yerel takvim gününe göre belirlenir.
        if (DateOnly.FromDateTime(match.Kickoff.ToLocalTime().DateTime) > DateOnly.FromDateTime(DateTime.Now))
            return AbsenceReport.PendingReport();

        var key = $"absences:v1:{match.Id}";
        var cached = await cache.GetAsync<AbsenceReport>(key);
        if (cached is not null)
            return cached;

        if (await MonthlyLimitReachedAsync(opt, ct))
        {
            // Sınır dolu: her istekte DB'ye sorulmasın diye kısa süre önbelleğe alınır (başarısızlıkla aynı süre).
            var limited = AbsenceReport.UnavailableReport();
            await cache.SetAsync(key, limited, FailureCacheTtl);
            return limited;
        }

        var model = string.IsNullOrWhiteSpace(opt.Model) ? gemini.Model : opt.Model;
        var usage = new SearchUsage();
        var started = Stopwatch.GetTimestamp();
        AbsenceReport report;
        TimeSpan ttl;
        try
        {
            report = await SearchAsync(match, model, usage, ct);
            ttl = TimeSpan.FromMinutes(Math.Max(1, opt.CacheMinutes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Absences for fixture {FixtureId} could not be researched; forecasting without them.", match.Id);
            report = AbsenceReport.UnavailableReport();
            ttl = FailureCacheTtl;
        }

        await RecordUsageAsync(match.Id, model, usage, Stopwatch.GetElapsedTime(started));
        await cache.SetAsync(key, report, ttl);
        return report;
    }

    // Sayaç okunamazsa (DB hatası) arama engellenmez: maliyet koruması, özelliği tamamen durdurmamalı.
    private async Task<bool> MonthlyLimitReachedAsync(AbsencesOptions opt, CancellationToken ct)
    {
        if (opt.MonthlySearchLimit <= 0)
            return false;
        try
        {
            var used = await usageStore.SearchQueriesThisMonthAsync(ct);
            if (used < opt.MonthlySearchLimit)
                return false;

            logger.LogWarning("Absence search skipped: monthly Google search limit reached ({Used}/{Limit}). Absences:MonthlySearchLimit",
                used, opt.MonthlySearchLimit);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Monthly search count could not be read; searching anyway.");
            return false;
        }
    }

    // Gemini cevap verdiyse (cevap sonradan ayrıştırılamasa bile token ve arama faturalanır) başarılı sayılır.
    // Fail-open ve CancellationToken.None: kayıt yazılamasa da, istemci bağlantıyı kapatsa da rapor dönülür.
    private async Task RecordUsageAsync(int fixtureId, string model, SearchUsage usage, TimeSpan duration)
    {
        try
        {
            var completion = usage.Completion;
            await usageStore.RecordSearchAsync(fixtureId, model, completion, completion is null ? 0 : usage.SearchQueries,
                usage.FailedStatusCode, duration, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Absence search usage for fixture {FixtureId} could not be recorded.", fixtureId);
        }
    }

    // SearchAsync'in faturalanan kısmı; exception olsa da GetAsync kaydedebilsin diye ayrı tutulur.
    private sealed class SearchUsage
    {
        public AiCompletion? Completion { get; set; }
        public int SearchQueries { get; set; }
        public int? FailedStatusCode { get; set; }
    }

    private async Task<AbsenceReport> SearchAsync(MatchSummary match, string model, SearchUsage usage, CancellationToken ct)
    {
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var user = $"""
            Maç: {match.HomeTeam} (ev) - {match.AwayTeam} (deplasman)
            Turnuva: {match.League} ({match.Country})
            Tarih: {match.Kickoff.ToLocalTime().ToString("dd MMMM yyyy HH:mm", tr)}
            Takım adları (team alanına birebir bunlardan birini yaz): "{match.HomeTeam}", "{match.AwayTeam}"
            """;

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
            tools = new[] { new { google_search = new { } } },
            generationConfig = new { maxOutputTokens = aiOptions.Value.MaxTokens }
        };

        using var res = await http.PostAsJsonAsync($"models/{model}:generateContent", body, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            usage.FailedStatusCode = (int)res.StatusCode;
            throw new InvalidOperationException($"Gemini search returned HTTP {(int)res.StatusCode}: {(raw.Length > 300 ? raw[..300] : raw)}");
        }

        using var doc = JsonDocument.Parse(raw);
        usage.Completion = ReadCompletion(doc.RootElement);
        var candidate = doc.RootElement.GetProperty("candidates")[0];
        usage.SearchQueries = CountSearchQueries(candidate);
        var text = candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts)
            ? string.Concat(parts.EnumerateArray()
                .Where(p => p.TryGetProperty("text", out _)
                            && !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
                .Select(p => p.GetProperty("text").GetString()))
            : "";

        var parsed = ParseResponse(text);
        var players = ParsePlayers(parsed, match);
        var sources = ParseSources(candidate);
        if (sources.Count == 0)
            sources = await linkChecker.FilterReachableAsync(ModelSources(parsed), ct);

        return new AbsenceReport(AbsenceReport.Ready, players, sources, DateTimeOffset.Now);
    }

    /*
     * Token sayıları (usageMetadata): arama sonuçları modele girdi olarak verildiği için toolUsePromptTokenCount girdiye,
     * düşünme (thoughtsTokenCount) çıktıya eklenir; ikisi de böyle faturalanır.
     */
    private static AiCompletion ReadCompletion(JsonElement root)
    {
        var u = root.TryGetProperty("usageMetadata", out var meta) ? meta : default;
        int? Sum(string a, string b)
        {
            var x = AiCompletion.ReadInt(u, a);
            var y = AiCompletion.ReadInt(u, b);
            return x is null && y is null ? null : (x ?? 0) + (y ?? 0);
        }
        return new AiCompletion("", Sum("promptTokenCount", "toolUsePromptTokenCount"), Sum("candidatesTokenCount", "thoughtsTokenCount"));
    }

    /*
     * Faturalanan Google araması sayısı: groundingMetadata.webSearchQueries içindeki boş olmayan, benzersiz sorgular
     * (Google boş sorguları saymaz). Gemini düşünme sırasında arama yaptığında groundingMetadata hiç gelmeyebiliyor; istem aramayı zorunlu tuttuğu için bu durumda en az 1 sorgu yapıldığı varsayılır (maliyet eksik görünmesin).
     */
    public static int CountSearchQueries(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("groundingMetadata", out var meta) || meta.ValueKind != JsonValueKind.Object)
            return 1;
        if (!meta.TryGetProperty("webSearchQueries", out var queries) || queries.ValueKind != JsonValueKind.Array)
            return meta.TryGetProperty("groundingChunks", out _) ? 1 : 0;

        return queries.EnumerateArray()
            .Where(q => q.ValueKind == JsonValueKind.String)
            .Select(q => q.GetString()!.Trim())
            .Where(q => q.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    // Model metninden JSON'u ayıklar (kod bloğu veya önündeki açıklama atlanır).
    private static RawResponse ParseResponse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new JsonException("Absence response contains no JSON object.");

        return JsonSerializer.Deserialize<RawResponse>(text[start..(end + 1)], JsonOpts)
               ?? throw new JsonException("Empty absence response.");
    }

    // Modelin yazdığı kaynaklar (yalnızca groundingChunks yoksa kullanılır, sonra açılabilirliği denetlenir).
    private static IReadOnlyList<AbsenceSource> ModelSources(RawResponse parsed) =>
        (parsed.Sources ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .Select(x => new AbsenceSource(x.Title?.Trim() ?? "", x.Url!.Trim()))
            .DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSources)
            .ToList();

    // Takım adını fikstürle eşler, durumu normalize eder, tekrarları atar.
    private static IReadOnlyList<TeamAbsence> ParsePlayers(RawResponse parsed, MatchSummary match)
    {
        return (parsed.Players ?? [])
            .Select(p => (Team: ResolveTeam(p.Team, match), p))
            .Where(x => x.Team is not null && !string.IsNullOrWhiteSpace(x.p.Player))
            .Select(x => new TeamAbsence(x.Team!, x.p.Player!.Trim(), NormalizeStatus(x.p.Status), LocalizedText.Of(x.p.Reason, x.p.ReasonEn)))
            .DistinctBy(a => (a.Team, a.Player.ToLowerInvariant()))
            .GroupBy(a => a.Team)
            .SelectMany(g => g.Take(MaxPlayersPerTeam))
            .ToList();
    }

    // Birebir eşleşme; olmazsa biri diğerini içeriyorsa (ör. "Trabzonspor A.Ş." → "Trabzonspor"). Tanınmayan takım atılır.
    private static string? ResolveTeam(string? team, MatchSummary match)
    {
        if (string.IsNullOrWhiteSpace(team))
            return null;
        var t = team.Trim();
        foreach (var name in new[] { match.HomeTeam, match.AwayTeam })
            if (string.Equals(t, name, StringComparison.OrdinalIgnoreCase))
                return name;
        foreach (var name in new[] { match.HomeTeam, match.AwayTeam })
            if (t.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(t, StringComparison.OrdinalIgnoreCase))
                return name;
        return null;
    }

    // Tanınmayan durum "Doubtful" sayılır: belirsizlik varsa şüpheli göstermek, kesin "oynamayacak" demekten güvenlidir.
    private static string NormalizeStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "suspended" => TeamAbsence.Suspended,
        "notinsquad" or "not_in_squad" or "not in squad" => TeamAbsence.NotInSquad,
        "injured" => TeamAbsence.Injured,
        _ => TeamAbsence.Doubtful
    };

    // groundingMetadata.groundingChunks[].web { uri, title }; en fazla MaxSources benzersiz kaynak.
    private static IReadOnlyList<AbsenceSource> ParseSources(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("groundingMetadata", out var meta)
            || !meta.TryGetProperty("groundingChunks", out var chunks)
            || chunks.ValueKind != JsonValueKind.Array)
            return [];

        return chunks.EnumerateArray()
            .Where(c => c.TryGetProperty("web", out _))
            .Select(c => c.GetProperty("web"))
            .Select(w => new AbsenceSource(
                w.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                w.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : ""))
            .Where(s => s.Url.Length > 0)
            .DistinctBy(s => s.Title.Length > 0 ? s.Title : s.Url)
            .Take(MaxSources)
            .ToList();
    }

    private sealed record RawResponse(List<RawPlayer>? Players, List<RawSource>? Sources);
    private sealed record RawSource(string? Title, string? Url);
    private sealed record RawPlayer(string? Team, string? Player, string? Status, string? Reason, string? ReasonEn);
}