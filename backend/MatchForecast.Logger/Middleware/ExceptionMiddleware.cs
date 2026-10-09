using MatchForecast.Logger.Notifications;
using MatchForecast.Models.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

            /*
             * Yalnızca 5xx hatalarda yöneticiye e-posta gider: dış servis (API-Football, AI) hataları ForecastException
             * ile 502/503/504, beklenmeyen hatalar 500 döner. 404 (maç yok) ve 401 gibi istemci kaynaklı durumlar
             * bildirim gerektirmez; aksi halde gelen kutusu gürültüyle dolar. IErrorNotifier kayıtlı değilse çağrı atlanır,
             * Smtp:Enabled=false ise Notify hiçbir şey yapmaz.
             */
            if (status >= StatusCodes.Status500InternalServerError)
                NotifyAdmin(context, ex, status, detail);

            // Frontend (api.ts) ProblemDetails içindeki "detail" alanını okur.
            context.Response.StatusCode = status;
            await Results.Problem(detail: detail, statusCode: status).ExecuteAsync(context);
        }
    }

    private void NotifyAdmin(HttpContext context, Exception ex, int status, string detail)
    {
        try
        {
            context.RequestServices.GetService<IErrorNotifier>()?.Notify(new ErrorNotification(
                DateTimeOffset.Now,
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString.Value,
                status,
                // Beklenmeyen hatada kullanıcıya genel mesaj gider ama yöneticinin gerçek mesajı görmesi gerekir.
                ex is ForecastException ? detail : ex.Message,
                ex.GetType().FullName ?? ex.GetType().Name,
                ex.StackTrace,
                context.TraceIdentifier));
        }
        catch (Exception notifyEx)
        {
            // Bildirim hatası asıl hatanın ProblemDetails cevabını asla bozmamalı.
            _logger.LogWarning(notifyEx, "Error notification could not be queued.");
        }
    }
}