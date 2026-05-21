using System.Text.Json;
using Services.Implementations;

namespace HotWaterGas_BE.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException apiException)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = apiException.StatusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = apiException.Message }));
        }
        catch (UnauthorizedAccessException unauthorizedException)
        {
            // Belt-and-suspenders: services should throw ApiException(401, ...) instead, but if an
            // UnauthorizedAccessException escapes, convert it to a proper 401 response here.
            _logger.LogWarning(
                unauthorizedException,
                "[ExceptionHandling] UnauthorizedAccessException escaped to middleware. " +
                "Services should throw ApiException(401, ...) instead. Message={Message}",
                unauthorizedException.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { message = "Yêu cầu xác thực." }));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled exception while processing request.");

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { message = "Đã xảy ra lỗi không mong muốn." }));
        }
    }
}
