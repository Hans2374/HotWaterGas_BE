using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _adminUserService;
    private readonly ICurrentUserService _currentUserService;

    public AdminUsersController(
        IAdminUserService adminUserService,
        ICurrentUserService currentUserService)
    {
        _adminUserService = adminUserService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isSuspended = null,
        CancellationToken cancellationToken = default)
    {
        var query = new AdminUserQueryRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            Role = role,
            IsSuspended = isSuspended
        };

        var response = await _adminUserService.GetUsersAsync(query, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUserDetail(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _adminUserService.GetUserDetailAsync(id, cancellationToken);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "User not found." });
        }
    }

    [HttpPatch("{id:guid}/suspension")]
    public async Task<IActionResult> ToggleSuspension(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var adminUserId = _currentUserService.UserId;
        if (!adminUserId.HasValue)
        {
            return Unauthorized(new { message = "Unable to identify admin user." });
        }

        try
        {
            await _adminUserService.ToggleSuspensionAsync(id, adminUserId.Value, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "User not found." });
        }
    }
}
