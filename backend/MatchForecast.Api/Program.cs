using NLog;
using NLog.Web;
using MatchForecast.Models.Common;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using MatchForecast.Api.Middleware;

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

    // OddsProvider:UseMock=true -> anahtarsız örnek veri; false -> API-Football (x-apisports-key gerekir)
    var oddsOpt = builder.Configuration.GetSection(OddsProviderOptions.Section).Get<OddsProviderOptions>() ?? new();
    if (oddsOpt.UseMock)
    {
        builder.Services.AddSingleton<IOddsProvider, MockOddsProvider>();
    }
    else
    {
        builder.Services.AddHttpClient<IOddsProvider, ApiFootballOddsProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<OddsProviderOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.DefaultRequestHeaders.Add("x-apisports-key", o.ApiKey);
        });
    }

    builder.Services.AddHttpClient<IForecastAiClient, ClaudeForecastClient>((sp, c) =>
    {
        var o = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        c.BaseAddress = new Uri(o.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(90);
        c.DefaultRequestHeaders.Add("x-api-key", o.ApiKey);
        c.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    });

    builder.Services.AddScoped<ForecastService>();

    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

    var app = builder.Build();

    // ForecastException -> kullanıcıya gösterilebilir ProblemDetails; diğer hatalar -> 500
    app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var reqLogger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

        if (ex is ForecastException fe)
        {
            reqLogger.LogWarning(ex, "ForecastException caught by handler: {Message} (Status {StatusCode})", fe.Message, fe.StatusCode);
        }
        else if (ex is not null)
        {
            reqLogger.LogError(ex, "Unhandled exception caught by handler: {Message}", ex.Message);
        }

        var (status, detail) = ex is ForecastException feEx
            ? (feEx.StatusCode, feEx.Message)
            : (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.");
        ctx.Response.StatusCode = status;
        await Results.Problem(detail: detail, statusCode: status).ExecuteAsync(ctx);
    }));

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

    app.UseMiddleware<ExceptionMiddleware>();
    app.UseMiddleware<LoggingMiddleware>();

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