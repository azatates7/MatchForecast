using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using NLog;
using NLog.Web;
using MatchForecast.Api.Options;
using MatchForecast.Api.Filter;
using MatchForecast.Logger.Extensions;
using MatchForecast.Logger.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StackExchange.Redis;
using MatchForecast.Api.Services.Security;
using MatchForecast.Api.Services.ApiFootball;
using MatchForecast.Api.Services.Forecast;
using MatchForecast.Api.Services.AI.Gemini;
using MatchForecast.Api.Services.AI.Claude;
using MatchForecast.Api.Services.AI.Ollama;
using MatchForecast.Api.Services.Email;
using MatchForecast.Api.Services.Redis;
using MatchForecast.Data.Configuration;
using MatchForecast.Api.Services.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MatchForecast.Data.Entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
logger.Debug("Initializing MatchForecast API host...");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Setup NLog as logging provider
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Supabase (PostgreSQL). Connection string repoda değil, user-secrets / env'de: ConnectionStrings:Supabase
    builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(
    builder.Configuration.GetConnectionString("Supabase"),
    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));

    builder.Services.AddOptions<AuthOptions>()
        .Bind(builder.Configuration.GetSection(AuthOptions.Section))
        .Validate(o => Uri.TryCreate(o.PasswordResetUrl, UriKind.Absolute, out _), "Auth:PasswordResetUrl mutlak bir adres olmalı.")
        .Validate(o => o.PasswordResetTokenMinutes > 0, "Auth:PasswordResetTokenMinutes 0'dan büyük olmalı.")
        .ValidateOnStart();
    builder.Services.AddSingleton<GoogleIdTokenValidator>();

    builder.Services.Configure<OddsProviderOptions>(builder.Configuration.GetSection(OddsProviderOptions.Section));
    builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));

    // JWT ayarları: eksik/kısa SecretKey ile uygulama hiç ayağa kalkmasın (ilk istekte değil, startup'ta hata).
    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.Section))
        .Validate(o => Encoding.UTF8.GetByteCount(o.SecretKey) >= 32, "Jwt:SecretKey en az 32 byte olmalı (HS256).")
        .Validate(o => o.ExpiryMinutes > 0 && o.RefreshTokenExpiryDays > 0, "Jwt:ExpiryMinutes ve Jwt:RefreshTokenExpiryDays 0'dan büyük olmalı.")
        .ValidateOnStart();
    builder.Services.AddSingleton<JwtTokenService>();

    // Şifreler PBKDF2 (HMAC-SHA512, 100.000 iterasyon, tuzlu) ile hash'lenir. ASP.NET Core'un içinde; ek paket gerekmez.
    builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
    builder.Services.AddScoped<AuthService>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

    // JwtBearer ayarları IOptions<JwtOptions> üzerinden lazy kurulur (HttpClient kayıtlarındaki gibi); testlerde override edilebilir.
    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
        {
            var o = jwt.Value;
            // Claim adları token'daki gibi kalsın ("role" -> uzun Microsoft URI'sine çevrilmesin); Name/Role bu adlardan okunur.
            bearer.MapInboundClaims = false;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = JwtTokenService.NameClaimType,
                RoleClaimType = JwtTokenService.RoleClaimType,
                ValidateIssuer = true,
                ValidIssuer = o.Issuer,
                ValidateAudience = true,
                ValidAudience = o.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = JwtTokenService.CreateSigningKey(o.SecretKey),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30) // Varsayılan 5 dk; süresi dolan token'ın 5 dk daha geçerli kalmaması için.
            };
        });

    // Fallback policy: [AllowAnonymous] olmayan TÜM endpoint'ler token ister. Yeni controller'lar varsayılan olarak korunur.
    builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
    builder.Services.AddProblemDetails();

    /*
     * Sağlık kontrolleri (ek paket gerekmez). "ready" etiketliler /healthz/ready'de çalışır. Zaman aşımı kısa tutulur:
     * Supabase duraklatılmışsa bağlantı denemesi uzun sürebilir, sağlık isteği askıda kalmasın.
     */
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"], timeout: TimeSpan.FromSeconds(5))
        .AddCheck<RedisHealthCheck>("redis", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

    // Controllers klasöründeki [ApiController] sınıfları otomatik keşfedilir; yeni endpoint için Program.cs'e dokunmaya gerek yok.
    builder.Services.AddControllers();

    builder.Services.AddOpenApi();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title = "MatchForecast API",
            Version = "v1",
            Description = "Futbol maçları ve LLM (Claude) destekli tahmin analizi API'si"
        });

        // Swagger UI "Authorize" butonu: ApiKey tipi değeri header'a olduğu gibi yazdığından değer "Bearer {token}" formatında girilir.
        c.AddSecurityDefinition(BearerSecurityOperationFilter.SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "GET /api/auth/token ile aldığınız token'ı \"Bearer {accessToken}\" formatında girin."
        });
        c.OperationFilter<BearerSecurityOperationFilter>();
    });

    builder.Services.AddHttpClient<IOddsProvider, ApiFootballOddsProvider>((sp, c) =>
    {
        var o = sp.GetRequiredService<IOptions<OddsProviderOptions>>().Value;
        c.BaseAddress = new Uri(o.BaseUrl);
        c.DefaultRequestHeaders.Add("x-apisports-key", o.ApiKey);
    });

    // AI sağlayıcısı Ai:Provider ile seçilir; iki sağlayıcının anahtarı user-secrets'ta aynı anda durabilir.
    var aiProvider = builder.Configuration[$"{AiOptions.Section}:{nameof(AiOptions.Provider)}"] ?? "Gemini";
    if (aiProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddHttpClient<IForecastAiClient, GeminiForecastClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<AiOptions>>().Value.Gemini;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(90);
            c.DefaultRequestHeaders.Add("x-goog-api-key", o.ApiKey);
        });
    }
    else if (aiProvider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddHttpClient<IForecastAiClient, ClaudeForecastClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<AiOptions>>().Value.Claude;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(90);
            c.DefaultRequestHeaders.Add("x-api-key", o.ApiKey);
            c.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        });
    }
    else if (aiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddHttpClient<IForecastAiClient, OllamaForecastClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<AiOptions>>().Value.Ollama;
            c.BaseAddress = new Uri(o.BaseUrl);
            // Yerel model yavaş olabilir (ilk istekte model belleğe de yüklenir); süre ayardan gelir.
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(o.ApiKey))
                c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.ApiKey);
        });
    }
    else
    {
        throw new InvalidOperationException($"Ai:Provider geçersiz: '{aiProvider}'. 'Gemini', 'Claude' veya 'Ollama' olmalı.");
    }

    /*
     * SMTP ile hata bildirimi. Enabled=false iken notifier yine kayıtlıdır ama Notify hiçbir şey yapmaz; böylece
     * testler ve yerel geliştirme mail ayarı olmadan çalışır. Enabled=true iken eksik ayarla uygulama başlamaz
     * (ilk hatada sessizce mail gitmemesi yerine startup'ta görünür hata).
     */
    builder.Services.AddOptions<SmtpOptions>()
        .Bind(builder.Configuration.GetSection(SmtpOptions.Section))
        .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Host), "Smtp:Host boş olamaz.")
        .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Username), "Smtp:Username (Gmail adresi) boş olamaz.")
        .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Password), "Smtp:Password (Gmail Uygulama Şifresi) boş olamaz.")
        .Validate(o => !o.Enabled || o.AdminEmails.Any(a => !string.IsNullOrWhiteSpace(a)), "Smtp:AdminEmails en az bir adres içermeli.")
        .ValidateOnStart();
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
    // Aynı instance hem IErrorNotifier (middleware kuyruğa yazar) hem hosted service (kuyruktan okuyup gönderir) olur.
    builder.Services.AddSingleton<EmailErrorNotifier>();
    builder.Services.AddSingleton<IErrorNotifier>(sp => sp.GetRequiredService<EmailErrorNotifier>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailErrorNotifier>());
    // Uygulama istek kabul etmeye hazır olduğunda bir kez "başlatıldı" maili (Smtp:SendStartupNotification).
    builder.Services.AddHostedService<StartupNotificationService>();

    // InstanceName boş olursa Cache Temizle "*" desenine düşüp Redis'teki her şeyi silerdi; bu yüzden startup'ta doğrulanır.
    builder.Services.AddOptions<RedisOptions>()
        .Bind(builder.Configuration.GetSection(RedisOptions.Section))
        .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Redis:ConnectionString boş olamaz.")
        .Validate(o => !string.IsNullOrWhiteSpace(o.InstanceName), "Redis:InstanceName boş olamaz.")
        .ValidateOnStart();

    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var o = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
        var config = ConfigurationOptions.Parse(o.ConnectionString);
        // Redis kapalıyken uygulama çökmesin, arka planda yeniden bağlansın.
        config.AbortOnConnectFail = false;
        // Bağlantı yokken komutlar kuyrukta beklemesin, hemen hata versin (servis bunu cache miss sayar).
        config.BacklogPolicy = BacklogPolicy.FailFast;
        return ConnectionMultiplexer.Connect(config);
    });
    builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();

    // Tahmin seçimi ayarları; bant ters girilirse (Min > Max) hiçbir seçenek banda girmez, bu yüzden startup'ta doğrulanır.
    builder.Services.AddOptions<ForecastOptions>()
        .Bind(builder.Configuration.GetSection(ForecastOptions.Section))
        .Validate(o => o.PredictionCount >= 1, "Forecast:PredictionCount en az 1 olmalı.")
        .Validate(o => o.MinPickProbability is >= 0 and <= 100 && o.MaxPickProbability is >= 0 and <= 100
                       && o.MinPickProbability <= o.MaxPickProbability,
            "Forecast:MinPickProbability/MaxPickProbability 0-100 arasında olmalı ve Min <= Max olmalı.")
        .ValidateOnStart();
    builder.Services.AddScoped<ForecastService>();

    /*
     * Rate limiting (ASP.NET Core'un yerleşik middleware'i, ek paket gerekmez). Policy'ler endpoint'lere
     * [EnableRateLimiting] ile bağlanır; attribute'u olmayan endpoint'ler etkilenmez. Ayrıntılar RateLimitOptions'ta.
     */
    var rateLimit = builder.Configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Caddy/Nginx arkasında gerçek IP için ASPNETCORE_FORWARDEDHEADERS_ENABLED=true gerekir; yoksa herkes proxy'nin IP'sini paylaşır.
        o.AddPolicy(RateLimitOptions.TokenPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimit.TokenPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimit.TokenWindowSeconds),
                QueueLimit = 0 // Limit aşılınca beklemeden 429.
            }));

        // Tek (global) sayaç; pencere dakikalık dilimlere bölünür, her dilimdeki hak tam 60 dk sonra geri gelir.
        o.AddSlidingWindowLimiter(RateLimitOptions.CacheClearPolicy, l =>
        {
            l.PermitLimit = rateLimit.CacheClearPermitLimit;
            l.Window = TimeSpan.FromMinutes(rateLimit.CacheClearWindowMinutes);
            l.SegmentsPerWindow = Math.Max(1, rateLimit.CacheClearWindowMinutes);
            l.QueueLimit = 0;
        });

        // Frontend (api.ts) hata mesajını ProblemDetails "detail" alanından okuduğu için 429 da aynı formatta döner.
        o.OnRejected = (ctx, ct) =>
        {
            var http = ctx.HttpContext;
            var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

            string detail;
            if (policy == RateLimitOptions.CacheClearPolicy)
            {
                // Kayan pencerede limiter'ın verdiği RetryAfter yalnızca bir dilim (1 dk) uzunluğundadır, gerçek bekleme süresi değildir; yanlış yönlendirmemek için Retry-After header'ı gönderilmez.
                detail = $"Cache temizleme {rateLimit.CacheClearWindowMinutes} dakika içinde en fazla {rateLimit.CacheClearPermitLimit} kez yapılabilir. Daha sonra tekrar deneyin.";
            }
            else if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                detail = $"Çok fazla istek gönderildi. {seconds} saniye sonra tekrar deneyin.";
            }
            else
            {
                detail = "Çok fazla istek gönderildi. Biraz sonra tekrar deneyin.";
            }

            return new ValueTask(Results.Problem(detail: detail, statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(http));
        };
    });

    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

    var app = builder.Build();

    // Bekleyen migration'ları uygular; ardından seed (AppDbContext.OnConfiguring → UseAsyncSeeding) çalışır.
    // Connection string yoksa (ör. CI'daki testler) atlanır. DB'ye ulaşılamazsa uygulama açılmaz (fail-fast).
    if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Supabase")))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Testler DbContext'i bellek içi sağlayıcıyla değiştirir; migration yalnızca gerçek (ilişkisel) veritabanında çalışır.
        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
            app.Logger.LogInformation("Veritabanı migration ve seed tamamlandı.");

            // Admin rolünde kullanıcı yoksa Admin:* ayarlarından ilk admini oluşturur.
            await AdminUserBootstrapper.EnsureAdminAsync(
                db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(), app.Configuration, app.Logger);
        }
    }
    
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous(); // Endpoint olduğu için fallback policy'ye takılmasın.
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "MatchForecast API v1");
        });
    }

    app.UseCors();

    // Middlewares from MatchForecast.Logger project
    app.UseMatchForecastExceptionHandling();
    app.UseMatchForecastLogging();

    // Logging'den sonra: 401 dönen istekler de loglanır.
    app.UseAuthentication();
    app.UseAuthorization();

    /*
     * Authorization'dan sonra: token'sız DELETE /api/cache istekleri önce 401 alır ve saatlik 2 hakkı tüketemez.
     * Token endpoint'i anonim olduğu için bu sıra onu etkilemez.
     */
    app.UseRateLimiter();

    app.MapControllers();

    // Liveness: süreç ayakta ve istek alıyor mu? Bağımlılıklara bakmaz (DB düştü diye container yeniden başlatılmasın).
    app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

    // Readiness: veritabanı ve Redis'e ulaşılıyor mu? DB yoksa 503, Redis yoksa 200 + "Degraded".
    app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = HealthResponseWriter.WriteAsync
    }).AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    logger.Fatal(ex, "Stopped program because of exception");
    throw;
}
catch (Exception ex)
{
    logger.Error(ex, "Stopped program because of exception during startup");
    throw;
}
finally
{
    LogManager.Shutdown();
}

public partial class Program { }