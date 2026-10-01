using MatchForecast.Logger.Middleware;
using Microsoft.AspNetCore.Builder;

namespace MatchForecast.Logger.Extensions;

/// <summary>
/// IApplicationBuilder extension methods to register MatchForecast logging/exception middlewares.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>Registers ExceptionMiddleware for centralized RFC 7807 ProblemDetails error handling.</summary>
    public static IApplicationBuilder UseMatchForecastExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionMiddleware>();

    /// <summary>Registers LoggingMiddleware for structured HTTP request/response logging with sensitive-field redaction.</summary>
    public static IApplicationBuilder UseMatchForecastLogging(this IApplicationBuilder app)
        => app.UseMiddleware<LoggingMiddleware>();
}
