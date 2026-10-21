using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MatchForecast.Api.Services.Health;

// Endpoint anonim olduğu için yalnızca durum ve açıklama yazılır; hata mesajları (host adı vb. içerebilir) dışarı verilmez,
// HealthCheckService tarafından log'a yazılır.
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Description })
        });
}