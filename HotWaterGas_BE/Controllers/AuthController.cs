using Microsoft.AspNetCore.Mvc;
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

    public AuthController(
        IAuthService authService,
        IRefreshTokenService refreshTokenService,
        IJwtTokenService jwtTokenService)
    {
        _authService = authService;
        _refreshTokenService = refreshTokenService;
        _jwtTokenService = jwtTokenService;
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

        var (refreshToken, _, expiresAtUtc) = await _refreshTokenService.GenerateRefreshTokenAsync(
            loginResponse.User.Id,
            GetClientIp(),
            GetUserAgent(),
            GetDeviceInfo(),
            cancellationToken: cancellationToken);

        _refreshTokenService.SetRefreshCookie(HttpContext, refreshToken, expiresAtUtc);

        var response = new LoginWithRefreshResponse
        {
            AccessToken = loginResponse.AccessToken,
            Role = loginResponse.Role,
            User = loginResponse.User,
            AccessTokenExpiresAt = _jwtTokenService.GetAccessTokenExpiry()
        };

        return Ok(response);
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
