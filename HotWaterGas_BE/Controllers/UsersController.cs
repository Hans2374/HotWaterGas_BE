using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IAuthService authService,
        ICurrentUserService currentUserService,
        ILogger<UsersController> logger)
    {
        _authService = authService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>
    /// Dev-only helper that logs every claim currently on the principal.
    /// Gated behind LogLevel.Debug so it never fires in production.
    /// </summary>
    private void LogAllClaimsForDebug()
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var allClaims = User.Claims.Select(c => $"[{c.Type}] = {c.Value}").ToList();

        _logger.LogDebug(
            "[UsersController] All JWT claims ({Count} total): {Claims}",
            allClaims.Count,
            string.Join(" | ", allClaims));

        _logger.LogDebug(
            "[UsersController] Resolved userId={UserId} IsAuthenticated={IsAuthenticated}",
            _currentUserService.UserId, _currentUserService.IsAuthenticated);
    }

    private IActionResult FailUnauthorized(string reason)
    {
        _logger.LogWarning("[UsersController] {Reason}", reason);
        LogAllClaimsForDebug();
        return Unauthorized(new AuthErrorResponse
        {
            Message = "Yêu cầu xác thực.",
            Code = "AUTH_REQUIRED"
        });
    }

    /// <summary>GET /api/users/me</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated)
        {
            return FailUnauthorized("GetMyProfile: user not authenticated.");
        }

        LogAllClaimsForDebug();

        var profile = await _authService.GetUserProfileAsync(
            _currentUserService.UserId!.Value, cancellationToken);
        return Ok(profile);
    }

    /// <summary>PUT /api/users/profile</summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { message = "Dữ liệu không hợp lệ.", errors = ModelState });
        }

        if (!_currentUserService.IsAuthenticated)
        {
            return FailUnauthorized("UpdateProfile: user not authenticated.");
        }

        var profile = await _authService.UpdateProfileAsync(
            _currentUserService.UserId!.Value, request, cancellationToken);
        return Ok(profile);
    }

    /// <summary>PUT /api/users/change-password</summary>
    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { message = "Dữ liệu không hợp lệ.", errors = ModelState });
        }

        if (!_currentUserService.IsAuthenticated)
        {
            return FailUnauthorized("ChangePassword: user not authenticated.");
        }

        var response = await _authService.ChangePasswordAsync(
            _currentUserService.UserId!.Value, request, cancellationToken);
        return Ok(response);
    }
}
