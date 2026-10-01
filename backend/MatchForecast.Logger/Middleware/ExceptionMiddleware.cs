using MatchForecast.Models.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MatchForecast.Logger.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // ForecastException kendi status kodunu (404, 502 vb.) ve kullanıcıya gösterilebilir mesajını taşır; diğer hatalar 500.
            var (status, detail) = ex is ForecastException fe
                ? (fe.StatusCode, fe.Message)
                : (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.");

            if (status >= StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(ex,
                    "Unhandled exception. Path: {Path}, Method: {Method}, Status: {Status}",
                    context.Request.Path,
                    context.Request.Method,
                    status);
            }
            else
            {
                _logger.LogWarning(ex,
                    "Handled exception. Path: {Path}, Method: {Method}, Status: {Status}",
                    context.Request.Path,
                    context.Request.Method,
                    status);
            }

            // Frontend (api.ts) ProblemDetails içindeki "detail" alanını okur.
            context.Response.StatusCode = status;
            await Results.Problem(detail: detail, statusCode: status).ExecuteAsync(context);
        }
    }
}
