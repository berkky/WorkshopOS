namespace WorkshopOS.Web.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            if (!headers.ContainsKey("X-Content-Type-Options"))
            {
                headers.XContentTypeOptions = "nosniff";
            }

            if (!headers.ContainsKey("X-Frame-Options") && !headers.ContainsKey("Content-Security-Policy"))
            {
                headers.XFrameOptions = "DENY";
            }

            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }

            if (!headers.ContainsKey("Permissions-Policy"))
            {
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}
