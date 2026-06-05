using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin/developers")]
[Authorize(Roles = "Admin")]
public class AdminDevelopersController : ControllerBase
{
    private readonly IAdminDeveloperService _adminDeveloperService;

    public AdminDevelopersController(IAdminDeveloperService adminDeveloperService)
    {
        _adminDeveloperService = adminDeveloperService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDevelopers(
        [FromQuery] GetAdminDevelopersRequest request,
        CancellationToken cancellationToken = default)
    {
        var pagedResult = await _adminDeveloperService.GetDevelopersAsync(request, cancellationToken);
        return Ok(pagedResult);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDeveloperById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var developer = await _adminDeveloperService.GetDeveloperByIdAsync(id, cancellationToken);

        if (developer is null)
        {
            return NotFound(new { message = "Developer not found." });
        }

        return Ok(developer);
    }

    [HttpPost]
    public async Task<IActionResult> CreateDeveloper(
        [FromBody] CreateDeveloperRequest request,
        CancellationToken cancellationToken = default)
    {
        var developer = await _adminDeveloperService.CreateDeveloperAsync(request, cancellationToken);
        return Ok(developer);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDeveloper(
        [FromRoute] Guid id,
        [FromBody] UpdateDeveloperRequest request,
        CancellationToken cancellationToken = default)
    {
        var developer = await _adminDeveloperService.UpdateDeveloperAsync(id, request, cancellationToken);
        return Ok(developer);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDeveloper(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminDeveloperService.DeleteDeveloperAsync(id, cancellationToken);
        return NoContent();
    }
}
