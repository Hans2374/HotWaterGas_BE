using System.Text;

namespace HotWaterGas_BE.Middleware;

/// <summary>
/// Temporary diagnostic middleware to verify OAuth correlation cookie emission.
/// This middleware logs Set-Cookie headers AFTER authentication middleware runs.
/// Remove after debugging is complete.
/// </summary>
public class OAuthCorrelationDiagnosticsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OAuthCorrelationDiagnosticsMiddleware> _logger;

    public OAuthCorrelationDiagnosticsMiddleware(
        RequestDelegate next,
        ILogger<OAuthCorrelationDiagnosticsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only log for OAuth endpoints
        if (!path.Contains("/auth/google/login", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains("/auth/google/callback", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Capture original response body stream
        var originalBodyStream = context.Response.Body;

        try
        {
            // Use a memory stream to capture response
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            // Call next middleware (this will trigger authentication)
            await _next(context);

            // Log diagnostics BEFORE copying response to original stream
            LogOAuthDiagnostics(context, path);

            // Copy captured response back to original stream
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private void LogOAuthDiagnostics(HttpContext context, string path)
    {
        var scheme = context.Request.Scheme;
        var host = context.Request.Host;
        var statusCode = context.Response.StatusCode;

        _logger.LogInformation(
            "[OAuth.Diagnostics] Path={Path} Scheme={Scheme} Host={Host} StatusCode={StatusCode}",
            path, scheme, host, statusCode);

        // Log all Set-Cookie headers
        if (context.Response.Headers.TryGetValue("Set-Cookie", out var setCookieHeaders))
        {
            foreach (var cookie in setCookieHeaders)
            {
                _logger.LogInformation("[OAuth.Diagnostics] Set-Cookie: {Cookie}", cookie);

                if (cookie.Contains("Correlation", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "[OAuth.Diagnostics] *** CORRELATION COOKIE FOUND: {Cookie}", cookie);
                }
            }
        }
        else
        {
            _logger.LogWarning("[OAuth.Diagnostics] NO Set-Cookie headers in response!");
        }

        // Log response location header (OAuth redirect)
        if (context.Response.Headers.TryGetValue("Location", out var locationHeaders))
        {
            _logger.LogInformation("[OAuth.Diagnostics] Location: {Location}", locationHeaders.ToString());
        }

        // For callback requests, log incoming cookies
        if (path.Contains("/callback", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[OAuth.Diagnostics] === CALLBACK REQUEST COOKIES ===");
            foreach (var cookie in context.Request.Cookies)
            {
                _logger.LogInformation(
                    "[OAuth.Diagnostics] Request Cookie: {Key}", cookie.Key);

                if (cookie.Key.Contains("Correlation", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "[OAuth.Diagnostics] *** CORRELATION COOKIE RECEIVED: {Key}={Value}",
                        cookie.Key, cookie.Value);
                }
            }
        }
    }
}

public static class OAuthCorrelationDiagnosticsMiddlewareExtensions
{
    public static IApplicationBuilder UseOAuthCorrelationDiagnostics(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<OAuthCorrelationDiagnosticsMiddleware>();
    }
}
