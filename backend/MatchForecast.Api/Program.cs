using NLog;
using NLog.Web;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using MatchForecast.Logger.Extensions;
using Microsoft.Extensions.Options;

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
        app.MapOpenApi();
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