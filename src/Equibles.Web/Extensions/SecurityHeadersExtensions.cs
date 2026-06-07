namespace Equibles.Web.Extensions;

public static class SecurityHeadersExtensions
{
    // Content-Security-Policy for the portal. Scripts/styles allow 'unsafe-inline' because the
    // views ship inline <script> blocks and inline styles; the remaining directives still add
    // defense-in-depth (no plugins, no framing, locked-down base/form targets). External
    // subresources are limited to the Google Fonts origins the layout loads (_Head.cshtml).
    private const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'unsafe-inline'; "
        + "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
        + "font-src 'self' https://fonts.gstatic.com; "
        + "img-src 'self' data: https:; "
        + "connect-src 'self'; "
        + "object-src 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'";

    // Adds baseline security response headers to every response. Placed early in the pipeline so
    // error responses and static files are covered too.
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(
            async (context, next) =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                await next();
            }
        );
}
