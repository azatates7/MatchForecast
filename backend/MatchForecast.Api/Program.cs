using MatchForecast.Api.Models;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OddsProviderOptions>(builder.Configuration.GetSection(OddsProviderOptions.Section));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));
builder.Services.AddMemoryCache();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var useMock = builder.Configuration.GetValue($"{OddsProviderOptions.Section}:UseMock", true);
if (useMock)
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
    var (status, detail) = ex is ForecastException fe
        ? (fe.StatusCode, fe.Message)
        : (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.");
    ctx.Response.StatusCode = status;
    await Results.Problem(detail: detail, statusCode: status).ExecuteAsync(ctx);
}));

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();

var api = app.MapGroup("/api/matches");

api.MapGet("", (DateOnly? date, IOddsProvider odds, CancellationToken ct) =>
    odds.GetMatchesAsync(date ?? DateOnly.FromDateTime(DateTime.Now), ct));

api.MapGet("/{id:int}/odds", async (int id, IOddsProvider odds, CancellationToken ct) =>
    await odds.GetOddsAsync(id, ct) is { } result ? Results.Ok(result) : Results.NotFound());

api.MapGet("/{id:int}/forecast", (int id, bool? refresh, ForecastService service, CancellationToken ct) =>
    service.GetForecastAsync(id, refresh ?? false, ct));

app.Run();
