using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;
    private readonly AuthTokenOptions _authOptions;

    public AuthController(
        IAuthService authService,
        IRefreshTokenService refreshTokenService,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration,
        IOptions<AuthTokenOptions> authOptions)
    {
        _authService = authService;
        _refreshTokenService = refreshTokenService;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
        _authOptions = authOptions.Value;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.RegisterAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var loginResponse = await _authService.LoginAsync(request, cancellationToken);

        var expiryDays = _authOptions.GetRefreshTokenExpiryDays(request.RememberMe);
        var (refreshToken, _, expiresAtUtc) = await _refreshTokenService.GenerateRefreshTokenAsync(
            loginResponse.User.Id,
            GetClientIp(),
            GetUserAgent(),
            GetDeviceInfo(),
            expiryDays,
            cancellationToken: cancellationToken);

        if (request.RememberMe)
        {
            _refreshTokenService.SetRefreshCookie(HttpContext, refreshToken, expiresAtUtc);
        }
        else
        {
            _refreshTokenService.SetSessionCookie(HttpContext, refreshToken);
        }

        _logger.LogInformation(
            "[Auth.Login] UserId={UserId} RememberMe={RememberMe} ExpiryDays={ExpiryDays}",
            loginResponse.User.Id, request.RememberMe, expiryDays);

        var response = new LoginWithRefreshResponse
        {
            AccessToken = loginResponse.AccessToken,
            Role = loginResponse.Role,
            User = loginResponse.User,
            AccessTokenExpiresAt = _jwtTokenService.GetAccessTokenExpiry(),
            RememberMe = request.RememberMe
        };

        return Ok(response);
    }

    [HttpGet("google/login")]
    public IActionResult GoogleLogin()
    {
        var googleClientId = _configuration["Authentication:Google:ClientId"];

        if (string.IsNullOrEmpty(googleClientId))
        {
            _logger.LogWarning("[Auth.Google] Google OAuth not configured");
            return BadRequest(new AuthErrorResponse
            {
                Message = "Google authentication is not configured.",
                Code = "GOOGLE_NOT_CONFIGURED"
            });
        }

        // CRITICAL: RedirectUri MUST be the backend callback endpoint, NOT the frontend URL.
        // After Google middleware processes the OAuth callback at /signin-google,
        // it will redirect to this RedirectUri (the MVC callback route).
        // The MVC callback then generates JWT and redirects to frontend.
        var callbackUrl = Url.Action(
            nameof(GoogleCallback),
            "Auth",
            null,
            Request.Scheme);

        _logger.LogInformation("[Auth.Google.Login] RedirectUri={RedirectUri}", callbackUrl);

        var properties = new AuthenticationProperties
        {
            RedirectUri = callbackUrl
        };

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("google/callback")]
    public async Task<IActionResult> GoogleCallback(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Auth.Google.Callback] === CALLBACK CONTROLLER REACHED ===");

        // IMPORTANT: Middleware has already authenticated the external user at /signin-google
        // and signed them in using the cookie scheme. We read from cookies here.
        _logger.LogInformation("[Auth.Google.Callback] Calling AuthenticateAsync with scheme: {Scheme}", CookieAuthenticationDefaults.AuthenticationScheme);
        var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation(
            "[Auth.Google.Callback] AuthenticateAsync result: Succeeded={Succeeded} None={None} Failure={Failure}",
            authenticateResult.Succeeded,
            authenticateResult.None,
            authenticateResult.Failure?.Message);

        if (authenticateResult.Principal != null)
        {
            var claims = authenticateResult.Principal.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
            _logger.LogInformation("[Auth.Google.Callback] Principal claims ({Count}): {Claims}",
                claims.Count, string.Join(", ", claims.Take(10)));
        }
        else
        {
            _logger.LogWarning("[Auth.Google.Callback] Principal is NULL");
        }

        if (!authenticateResult.Succeeded)
        {
            _logger.LogWarning(
                "[Auth.Google.Callback] Authentication failed: {Failure}",
                authenticateResult.Failure?.Message);

            var errorUrl = _configuration["Frontend:GoogleAuthErrorUrl"]
                ?? _configuration["Frontend:BaseUrl"]
                ?? "http://localhost:5173";

            return Redirect($"{errorUrl}?error=google_auth_failed");
        }

        // Extract Google claims from the principal (set by middleware after Google auth)
        var googleId = authenticateResult.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? authenticateResult.Principal?.FindFirstValue("sub")
            ?? authenticateResult.Principal?.FindFirstValue("http://schemas.google.com/claims/googleid")
            ?? string.Empty;
        var email = authenticateResult.Principal?.FindFirstValue(ClaimTypes.Email)
            ?? authenticateResult.Principal?.FindFirstValue("email")
            ?? authenticateResult.Principal?.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
            ?? string.Empty;
        var displayName = authenticateResult.Principal?.FindFirstValue(ClaimTypes.Name)
            ?? authenticateResult.Principal?.FindFirstValue("name")
            ?? authenticateResult.Principal?.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")
            ?? string.Empty;

        _logger.LogInformation(
            "[Auth.Google.Callback] Extracted claims - googleId={GoogleId}, email={Email}, displayName={DisplayName}",
            googleId, email, displayName);

        if (string.IsNullOrEmpty(googleId) || string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("[Auth.Google.Callback] Missing required Google claims - googleId={GoogleId}, email={Email}", googleId, email);

            var errorUrl = _configuration["Frontend:GoogleAuthErrorUrl"]
                ?? _configuration["Frontend:BaseUrl"]
                ?? "http://localhost:5173";

            return Redirect($"{errorUrl}?error=missing_google_claims");
        }

        try
        {
            _logger.LogInformation("[Auth.Google.Callback] Calling GoogleAuthAsync service...");

            // Process the Google authentication (creates/updates user)
            var response = await _authService.GoogleAuthAsync(googleId, email, displayName, cancellationToken);

            _logger.LogInformation(
                "[Auth.Google.Callback] GoogleAuthAsync completed - UserId={UserId} Role={Role} IsNewUser={IsNewUser} TokenLength={TokenLength}",
                response.User.Id, response.Role, response.IsNewUser, response.AccessToken?.Length ?? 0);

            // Sign out of the external cookie (cleanup temporary OAuth state)
            _logger.LogInformation("[Auth.Google.Callback] Signing out of external cookie...");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Generate refresh token
            _logger.LogInformation("[Auth.Google.Callback] Generating refresh token...");
            var (refreshToken, _, expiresAtUtc) = await _refreshTokenService.GenerateRefreshTokenAsync(
                response.User.Id,
                GetClientIp(),
                GetUserAgent(),
                GetDeviceInfo(),
                cancellationToken: cancellationToken);

            _refreshTokenService.SetRefreshCookie(HttpContext, refreshToken, expiresAtUtc);

            // Redirect to frontend success page with auth data
            var successUrl = _configuration["Frontend:GoogleAuthSuccessUrl"]
                ?? _configuration["Frontend:BaseUrl"]
                ?? "http://localhost:5173";

            // Ensure we have valid values
            var token = response.AccessToken ?? string.Empty;
            var role = response.Role ?? "Customer";
            var expiresAt = response.AccessTokenExpiresAt.ToString("O");
            var isNewUser = response.IsNewUser.ToString().ToLowerInvariant();

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("[Auth.Google.Callback] AccessToken is null or empty!");
                throw new InvalidOperationException("Failed to generate access token");
            }

            var redirectUrl = $"{successUrl}?token={Uri.EscapeDataString(token)}&expiresAt={Uri.EscapeDataString(expiresAt)}&role={Uri.EscapeDataString(role)}&isNewUser={isNewUser}";

            _logger.LogInformation("[Auth.Google.Callback] === REDIRECTING TO FRONTEND ===");
            _logger.LogInformation("[Auth.Google.Callback] RedirectUrl: {RedirectUrl}", redirectUrl);

            return Redirect(redirectUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auth.Google.Callback] Error processing Google auth for email={Email}", email);

            var errorUrl = _configuration["Frontend:GoogleAuthErrorUrl"]
                ?? _configuration["Frontend:BaseUrl"]
                ?? "http://localhost:5173";

            return Redirect($"{errorUrl}?error=auth_processing_failed");
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = _refreshTokenService.GetRefreshTokenFromCookie(HttpContext);

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new AuthErrorResponse
            {
                Message = "No refresh token provided.",
                Code = "MISSING_TOKEN"
            });
        }

        var (isValid, token, errorCode) = await _refreshTokenService.ValidateTokenAsync(refreshToken, cancellationToken);

        if (!isValid || token is null)
        {
            _refreshTokenService.ClearRefreshCookie(HttpContext);

            return Unauthorized(new AuthErrorResponse
            {
                Message = GetErrorMessage(errorCode),
                Code = errorCode ?? "INVALID_TOKEN"
            });
        }

        if (await _refreshTokenService.IsTokenReusedAsync(token.TokenHash, cancellationToken))
        {
            _logger.LogCritical(
                "[RefreshToken.ReuseDetected] Possible token theft! TokenFamilyId={TokenFamilyId} UserId={UserId}",
                token.TokenFamilyId, token.UserId);

            await _refreshTokenService.RevokeTokenFamilyAsync(token.TokenFamilyId, GetClientIp(), cancellationToken);
            _refreshTokenService.ClearRefreshCookie(HttpContext);

            return Unauthorized(new AuthErrorResponse
            {
                Message = "Session invalidated due to security concern.",
                Code = "SECURITY_CONCERN"
            });
        }

        var newAccessToken = _authService.GenerateAccessTokenForRefresh(token.User);
        var (newToken, newPlainToken) = await _refreshTokenService.RotateTokenAsync(
            token,
            GetClientIp(),
            GetUserAgent(),
            GetDeviceInfo(),
            cancellationToken);

        if (newToken is null || newPlainToken is null)
        {
            return StatusCode(500, new AuthErrorResponse
            {
                Message = "Failed to rotate token.",
                Code = "ROTATION_FAILED"
            });
        }

        _refreshTokenService.SetRefreshCookie(HttpContext, newPlainToken, newToken.ExpiresAtUtc);

        var response = new RefreshResponse
        {
            AccessToken = newAccessToken,
            AccessTokenExpiresAt = _jwtTokenService.GetAccessTokenExpiry()
        };

        _logger.LogInformation(
            "[RefreshToken.Success] UserId={UserId} TokenFamilyId={TokenFamilyId}",
            token.UserId, token.TokenFamilyId);

        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = _refreshTokenService.GetRefreshTokenFromCookie(HttpContext);

        if (!string.IsNullOrEmpty(refreshToken))
        {
            var tokenHash = await _refreshTokenService.HashTokenAsync(refreshToken);
            var token = await _refreshTokenService.GetTokenByHashAsync(tokenHash, cancellationToken);

            if (token is not null)
            {
                await _refreshTokenService.RevokeTokenAsync(token, GetClientIp(), cancellationToken);
            }
        }

        _refreshTokenService.ClearRefreshCookie(HttpContext);

        return Ok(new MessageResponse { Message = "Logged out successfully." });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        var refreshToken = _refreshTokenService.GetRefreshTokenFromCookie(HttpContext);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new AuthErrorResponse
            {
                Message = "Not authenticated.",
                Code = "AUTH_REQUIRED"
            });
        }

        var tokenHash = await _refreshTokenService.HashTokenAsync(refreshToken);
        var currentToken = await _refreshTokenService.GetTokenByHashAsync(tokenHash, cancellationToken);

        if (currentToken is null)
        {
            return Unauthorized(new AuthErrorResponse
            {
                Message = "Invalid session.",
                Code = "INVALID_TOKEN"
            });
        }

        var sessions = await _refreshTokenService.GetUserActiveSessionsAsync(currentToken.UserId, cancellationToken);

        var sessionResponses = sessions.Select(s => new AuthSessionInfoResponse
        {
            SessionId = s.Id,
            DeviceInfo = s.DeviceInfo ?? "Unknown",
            CreatedAtUtc = s.CreatedAtUtc,
            LastUsedAtUtc = s.LastUsedAtUtc,
            IsCurrent = s.Id == currentToken.Id
        }).ToList();

        return Ok(sessionResponses);
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var refreshToken = _refreshTokenService.GetRefreshTokenFromCookie(HttpContext);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new AuthErrorResponse
            {
                Message = "Not authenticated.",
                Code = "AUTH_REQUIRED"
            });
        }

        var tokenHash = await _refreshTokenService.HashTokenAsync(refreshToken);
        var currentToken = await _refreshTokenService.GetTokenByHashAsync(tokenHash, cancellationToken);

        if (currentToken is null)
        {
            return Unauthorized(new AuthErrorResponse
            {
                Message = "Invalid session.",
                Code = "INVALID_TOKEN"
            });
        }

        await _refreshTokenService.RevokeTokenFamilyAsync(currentToken.TokenFamilyId, GetClientIp(), cancellationToken);
        _refreshTokenService.ClearRefreshCookie(HttpContext);

        _logger.LogInformation("[RefreshToken.LogoutAll] UserId={UserId} TokenFamilyId={TokenFamilyId}",
            currentToken.UserId, currentToken.TokenFamilyId);

        return Ok(new MessageResponse { Message = "All sessions logged out." });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.VerifyEmailAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.ResendVerificationAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("forgot-password/request")]
    public async Task<IActionResult> ForgotPasswordRequest([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.ForgotPasswordRequestAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("forgot-password/verify")]
    public async Task<IActionResult> ForgotPasswordVerify([FromBody] ForgotPasswordVerifyRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.ForgotPasswordVerifyAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("forgot-password/reset")]
    public async Task<IActionResult> ForgotPasswordReset([FromBody] ForgotPasswordResetRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.ForgotPasswordResetAsync(request, cancellationToken);
        return Ok(response);
    }

    private string? GetClientIp()
    {
        var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private string? GetUserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.FirstOrDefault();
    }

    private string? GetDeviceInfo()
    {
        var userAgent = GetUserAgent();
        if (string.IsNullOrEmpty(userAgent))
            return "Unknown";

        if (userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
            return "Mobile";
        if (userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
            return "Tablet";
        return "Desktop";
    }

    private static string GetErrorMessage(string? errorCode)
    {
        return errorCode switch
        {
            "MISSING_TOKEN" => "No refresh token provided.",
            "INVALID_TOKEN" => "Invalid or unknown refresh token.",
            "REVOKED_TOKEN" => "This session has been revoked.",
            "EXPIRED_TOKEN" => "Refresh token has expired. Please log in again.",
            _ => "Token validation failed."
        };
    }

    private ILogger<AuthController> _logger => HttpContext.RequestServices.GetRequiredService<ILogger<AuthController>>();
}
