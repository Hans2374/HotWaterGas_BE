using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Services.DTOs;

namespace HotWaterGas_BE.Config;

public static class JwtSetupExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtTokenOptions.SectionName).Get<JwtTokenOptions>()
            ?? throw new InvalidOperationException("Jwt configuration is missing.");

        if (string.IsNullOrEmpty(jwtOptions.Key))
        {
            throw new InvalidOperationException("Jwt:Key is not configured.");
        }

        var googleClientId = configuration["Authentication:Google:ClientId"];
        var googleEnabled = !string.IsNullOrEmpty(googleClientId);

        // Build the authentication builder - this must only be called once
        var authBuilder = services
            .AddAuthentication(options =>
            {
                // JWT is primary for API authentication/authorization
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // DefaultChallengeScheme must be compatible with the schemes we use
                // For Google OAuth, we explicitly specify the scheme in Challenge() call
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                // Minimal cookie for OAuth handshake only - never used for permanent auth
                options.Cookie.Name = "ExternalAuth";
                options.Cookie.HttpOnly = true;
                // Required for cross-origin OAuth flows (frontend on 5173, backend on 7140)
                // Secure=Always is required because SameSite=None requires Secure flag
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Short expiration - just enough for the OAuth round-trip
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                // Sliding expiration not needed for short-lived OAuth cookie
                options.SlidingExpiration = false;
                // Don't redirect to login page - let the callback handle errors
                options.LoginPath = "/api/auth/google/error";
                options.AccessDeniedPath = "/api/auth/google/error";
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                // ── OnAuthenticationFailed ────────────────────────────────────────────
                // Fires when token parsing or validation throws. Covers expired,
                // malformed, and tampered tokens before the challenge is sent.
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        var exceptionType = context.Exception.GetType().Name;
                        var path = context.HttpContext.Request.Path.Value;
                        var method = context.HttpContext.Request.Method;

                        // SecurityTokenExpiredException — structurally valid but past expiry
                        if (context.Exception is SecurityTokenExpiredException expired)
                        {
                            logger.LogWarning(
                                "JWT token expired. Path={Path} Method={Method} ExpiredAt={ExpiredAt}",
                                path, method, expired.Expires);

                            WriteAuthErrorAsync(
                                context,
                                StatusCodes.Status401Unauthorized,
                                "Session expired. Please log in again.",
                                "TOKEN_EXPIRED").Wait();

                            context.NoResult();
                            return Task.CompletedTask;
                        }

                        // Malformed / tampered token (invalid base64, wrong segment count,
                        // signature mismatch, unknown signing key)
                        if (context.Exception is SecurityTokenSignatureKeyNotFoundException
                            || context.Exception is SecurityTokenInvalidSignatureException
                            || context.Exception is SecurityTokenInvalidSigningKeyException
                            || context.Exception is SecurityTokenException
                            || context.Exception is ArgumentException)
                        {
                            logger.LogWarning(
                                "JWT token invalid. Path={Path} Method={Method} ExceptionType={ExceptionType}",
                                path, method, exceptionType);

                            WriteAuthErrorAsync(
                                context,
                                StatusCodes.Status401Unauthorized,
                                "Invalid authentication token.",
                                "INVALID_TOKEN").Wait();

                            context.NoResult();
                            return Task.CompletedTask;
                        }

                        // Catch-all for any unexpected auth exception
                        logger.LogWarning(
                            "JWT authentication failed. Path={Path} Method={Method} ExceptionType={ExceptionType}",
                            path, method, exceptionType);

                        WriteAuthErrorAsync(
                            context,
                            StatusCodes.Status401Unauthorized,
                            "Authentication failed.",
                            "AUTH_FAILED").Wait();

                        context.NoResult();
                        return Task.CompletedTask;
                    },

                    // ── OnChallenge ──────────────────────────────────────────────────
                    // Fires when the auth middleware issues a 401 challenge.
                    // Handles missing token, wrong scheme, and any case that bypassed
                    // OnAuthenticationFailed. Prevents raw HTML / default payloads leaking.
                    OnChallenge = context =>
                    {
                        // Skip if already handled (e.g., OnAuthenticationFailed suppressed it)
                        if (context.Handled)
                        {
                            return Task.CompletedTask;
                        }

                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        var path = context.HttpContext.Request.Path.Value;
                        var method = context.HttpContext.Request.Method;

                        // OnChallenge fires when no valid bearer token reached the validation pipeline.
                        // Classify the failure into precise categories to return the correct error code.
                        var authHeader = context.HttpContext.Request.Headers.Authorization.ToString();

                        // Case A: Authorization header is entirely absent
                        if (string.IsNullOrEmpty(authHeader))
                        {
                            logger.LogWarning(
                                "Authentication required — no Authorization header provided. Path={Path} Method={Method}",
                                path, method);

                            return WriteChallengeErrorAsync(
                                context,
                                "Authentication required.",
                                "AUTH_REQUIRED");
                        }

                        // Token must start with "Bearer " (case-insensitive)
                        var bearerPrefix = "Bearer ";
                        if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            logger.LogWarning(
                                "Invalid authorization scheme. Path={Path} Method={Method}",
                                path, method);

                            return WriteChallengeErrorAsync(
                                context,
                                "Invalid authorization scheme.",
                                "INVALID_TOKEN");
                        }

                        // Cases B & C: "Bearer" with no token OR whitespace-only token
                        var token = authHeader.Length > bearerPrefix.Length
                            ? authHeader.Substring(bearerPrefix.Length)
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(token))
                        {
                            logger.LogWarning(
                                "Bearer token missing or whitespace-only. Path={Path} Method={Method}",
                                path, method);

                            return WriteChallengeErrorAsync(
                                context,
                                "Invalid authentication token.",
                                "INVALID_TOKEN");
                        }

                        // Case D/E (defensive): Token was provided but still triggered OnChallenge.
                        // This means it was structurally present but failed deeper validation.
                        // If we reach here, OnAuthenticationFailed either didn't fire or
                        // the token bypassed the validation path entirely.
                        logger.LogWarning(
                            "Authentication challenge issued — token present but invalid. Path={Path} Method={Method}",
                            path, method);

                        return WriteChallengeErrorAsync(
                            context,
                            "Invalid authentication token.",
                            "INVALID_TOKEN");
                    }
                };
            });

        // Add Google authentication if configured
        if (googleEnabled)
        {
            authBuilder.AddGoogle(options =>
                {
                    options.ClientId = googleClientId!;
                    options.ClientSecret = configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
                    options.CallbackPath = "/api/auth/google/callback";
                    options.SaveTokens = false;

                    // Configure correlation cookie for cross-origin OAuth (frontend/backend on different ports)
                    // Secure=Always is required because SameSite=None requires Secure flag
                    options.CorrelationCookie.Name = "GoogleOAuth.Correlation";
                    options.CorrelationCookie.SameSite = SameSiteMode.None;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.CorrelationCookie.HttpOnly = true;

                    options.Events.OnRedirectToAuthorizationEndpoint = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        logger.LogInformation(
                            "[Google.OAuth] Redirecting to Google - Url={Url} Scheme={Scheme} Host={Host}",
                            context.RedirectUri, context.Request.Scheme, context.Request.Host);

                        return Task.CompletedTask;
                    };
                    options.Events.OnCreatingTicket = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        // Map Google claims to our standard claims
                        var googleId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? context.Principal?.FindFirstValue("sub");
                        var email = context.Principal?.FindFirstValue(ClaimTypes.Email)
                            ?? context.Principal?.FindFirstValue("email");
                        var displayName = context.Principal?.FindFirstValue(ClaimTypes.Name)
                            ?? context.Principal?.FindFirstValue("name");
                        var avatarUrl = context.Principal?.FindFirstValue("picture");

                        // Store extracted values in tokens for the callback to access
                        context.Properties.SetString("google_id", googleId ?? string.Empty);
                        context.Properties.SetString("google_email", email ?? string.Empty);
                        context.Properties.SetString("google_name", displayName ?? string.Empty);
                        context.Properties.SetString("google_avatar", avatarUrl ?? string.Empty);

                        logger.LogInformation(
                            "[Google.OAuth] Ticket created - GoogleId={GoogleId} Email={Email}",
                            googleId, email);

                        return Task.CompletedTask;
                    };
                    options.Events.OnRemoteFailure = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        logger.LogWarning(
                            "[Google.OAuth] Remote failure - Error={Error}",
                            context.Failure?.Message ?? "Unknown");

                        context.HandleResponse();

                        // Redirect to frontend error page
                        var frontendUrl = configuration["Frontend:GoogleAuthErrorUrl"]
                            ?? "http://localhost:5173/auth/google/error";

                        context.Response.Redirect($"{frontendUrl}?error={Uri.EscapeDataString(context.Failure?.Message ?? "oauth_failed")}");

                        return Task.CompletedTask;
                    };
                    options.Events.OnAccessDenied = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        logger.LogWarning("[Google.OAuth] Access denied");

                        context.HandleResponse();

                        var frontendUrl = configuration["Frontend:GoogleAuthErrorUrl"]
                            ?? "http://localhost:5173/auth/google/error";

                        context.Response.Redirect($"{frontendUrl}?error=access_denied");

                        return Task.CompletedTask;
                    };
                });
        }

        services.AddAuthorization();

        return services;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a JSON auth error response inside OnAuthenticationFailed and suppresses
    /// further challenge processing by calling context.NoResult().
    /// Includes Response.HasStarted guard to prevent double-write in edge cases.
    /// </summary>
    private static Task WriteAuthErrorAsync(
        AuthenticationFailedContext context,
        int statusCode,
        string message,
        string code)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = statusCode;

        var payload = new Services.DTOs.AuthErrorResponse
        {
            Message = message,
            Code = code
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return context.Response.WriteAsync(json, default);
    }

    /// <summary>
    /// Writes a JSON auth error response inside OnChallenge and suppresses the default
    /// challenge response by calling context.HandleResponse().
    /// Includes Response.HasStarted guard to prevent double-write in edge cases.
    /// </summary>
    private static Task WriteChallengeErrorAsync(
        JwtBearerChallengeContext context,
        string message,
        string code)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var payload = new Services.DTOs.AuthErrorResponse
        {
            Message = message,
            Code = code
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        context.Response.WriteAsync(json, default).Wait();
        context.HandleResponse();
        return Task.CompletedTask;
    }
}
