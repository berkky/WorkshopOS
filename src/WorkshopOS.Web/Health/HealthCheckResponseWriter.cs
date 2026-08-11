using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WorkshopOS.Web.Health;

internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteMinimalResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        var payload = JsonSerializer.Serialize(new { status = "Healthy" }, SerializerOptions);
        if (report.Status != HealthStatus.Healthy)
        {
            payload = JsonSerializer.Serialize(new { status = "Unhealthy" }, SerializerOptions);
        }

        return context.Response.WriteAsync(payload);
    }
}
