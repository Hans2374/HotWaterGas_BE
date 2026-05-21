using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Services.Interfaces;

namespace Services.Implementations;

/// <summary>
/// Scoped service that reads and caches authenticated user identity from the
/// current HttpContext.User principal.
///
/// Claim resolution order (stops at first non-null value):
///   1. ClaimTypes.NameIdentifier  — standard "nameidentifier" URI; used by new tokens
///   2. "UserId"                  — legacy literal from older token versions
///   3. "sub"                     — plain "sub" string used by some JWT libraries
///
/// Role resolution order:
///   1. ClaimTypes.Role            — standard role claim
///
/// Email resolution:
///   1. ClaimTypes.Email          — standard email claim
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CurrentUserService> _logger;

    private readonly Guid? _userId;
    private readonly string? _email;
    private readonly string? _role;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        ILogger<CurrentUserService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;

        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            _userId = null;
            _email = null;
            _role = null;
            return;
        }

        _userId = ExtractUserId(principal);
        _email = principal.FindFirstValue(ClaimTypes.Email);
        _role = principal.FindFirstValue(ClaimTypes.Role);

        LogResolvedIdentity();
    }

    public Guid? UserId => _userId;
    public string? Email => _email;
    public string? Role => _role;
    public bool IsAuthenticated => _userId.HasValue;

    private static Guid? ExtractUserId(ClaimsPrincipal principal)
    {
        // Priority 1: standard NameIdentifier claim
        // JwtSecurityTokenHandler serialises ClaimTypes.NameIdentifier to this URI
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out var id))
            return id;

        // Priority 2: legacy "UserId" string literal (pre-fix token versions)
        value = principal.FindFirstValue("UserId");
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out id))
            return id;

        // Priority 3: plain "sub" string claim
        value = principal.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out id))
            return id;

        return null;
    }

    private void LogResolvedIdentity()
    {
        // Log only when the resolved identity is valid — avoids noisy logs for every request
        if (_userId.HasValue)
        {
            _logger.LogDebug(
                "[CurrentUser] Resolved UserId={UserId} Email={Email} Role={Role}",
                _userId.Value, _email ?? "(null)", _role ?? "(null)");
        }
        else
        {
            _logger.LogWarning(
                "[CurrentUser] Authenticated principal but no resolvable user ID claim. " +
                "Ensure tokens are issued with ClaimTypes.NameIdentifier. " +
                "Set 'Microsoft.AspNetCore': 'Debug' in Logging to see all claim types.");
        }
    }
}
