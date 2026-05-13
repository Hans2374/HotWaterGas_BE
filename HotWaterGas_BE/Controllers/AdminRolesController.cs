using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin/roles")]
[Authorize(Roles = "Admin")]
public class AdminRolesController : ControllerBase
{
    private readonly IAdminRoleService _adminRoleService;

    public AdminRolesController(IAdminRoleService adminRoleService)
    {
        _adminRoleService = adminRoleService;
    }

    [HttpGet]
    public async Task<IActionResult> GetRoles(
        [FromQuery] GetAdminRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        var pagedResult = await _adminRoleService.GetRolesAsync(request, cancellationToken);
        return Ok(pagedResult);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRoleById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var role = await _adminRoleService.GetRoleByIdAsync(id, cancellationToken);

        if (role is null)
        {
            return NotFound(new { message = "Role not found." });
        }

        return Ok(role);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRole(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = await _adminRoleService.CreateRoleAsync(request, cancellationToken);
        return Ok(role);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRole(
        [FromRoute] Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = await _adminRoleService.UpdateRoleAsync(id, request, cancellationToken);
        return Ok(role);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRole(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminRoleService.DeleteRoleAsync(id, cancellationToken);
        return NoContent();
    }
}
