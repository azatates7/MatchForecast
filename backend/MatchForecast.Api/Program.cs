using System.Text;
using NLog;
using NLog.Web;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using MatchForecast.Api.Swagger;
using MatchForecast.Logger.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
logger.Debug("Initializing MatchForecast API host...");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Setup NLog as logging provider
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    builder.Services.Configure<OddsProviderOptions>(builder.Configuration.GetSection(OddsProviderOptions.Section));
    builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));
    builder.Services.AddMemoryCache();

    // JWT ayarları: eksik/kısa SecretKey ile uygulama hiç ayağa kalkmasın (ilk istekte değil, startup'ta hata).
    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.Section))
        .Validate(o => Encoding.UTF8.GetByteCount(o.SecretKey) >= 32, "Jwt:SecretKey en az 32 byte olmalı (HS256).")
        .ValidateOnStart();
    builder.Services.AddSingleton<JwtTokenService>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

    // JwtBearer ayarları IOptions<JwtOptions> üzerinden lazy kurulur (HttpClient kayıtlarındaki gibi); testlerde override edilebilir.
    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
        {
            var o = jwt.Value;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
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

        // Swagger UI'da "Authorize" butonu. ApiKey tipi girilen değeri Authorization header'ına olduğu gibi yazar,
        // bu yüzden değer "Bearer {token}" formatında girilir (Http/bearer tipi öneki kendisi eklerdi).
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

    builder.Services.AddHttpClient<IForecastAiClient, GeminiForecastClient>((sp, c) =>
    {
        var o = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        c.BaseAddress = new Uri(o.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(90);
        c.DefaultRequestHeaders.Add("x-goog-api-key", o.ApiKey);
    });

    builder.Services.AddScoped<ForecastService>();

    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

    var app = builder.Build();

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

    app.MapControllers();

    app.Run();
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