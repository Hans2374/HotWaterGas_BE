using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin/publishers")]
[Authorize(Roles = "Admin")]
public class AdminPublishersController : ControllerBase
{
    private readonly IAdminPublisherService _adminPublisherService;

    public AdminPublishersController(IAdminPublisherService adminPublisherService)
    {
        _adminPublisherService = adminPublisherService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPublishers(
        [FromQuery] GetAdminPublishersRequest request,
        CancellationToken cancellationToken = default)
    {
        var pagedResult = await _adminPublisherService.GetPublishersAsync(request, cancellationToken);
        return Ok(pagedResult);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetPublisherById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var publisher = await _adminPublisherService.GetPublisherByIdAsync(id, cancellationToken);

        if (publisher is null)
        {
            return NotFound(new { message = "Publisher not found." });
        }

        return Ok(publisher);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePublisher(
        [FromBody] CreatePublisherRequest request,
        CancellationToken cancellationToken = default)
    {
        var publisher = await _adminPublisherService.CreatePublisherAsync(request, cancellationToken);
        return Ok(publisher);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdatePublisher(
        [FromRoute] Guid id,
        [FromBody] UpdatePublisherRequest request,
        CancellationToken cancellationToken = default)
    {
        var publisher = await _adminPublisherService.UpdatePublisherAsync(id, request, cancellationToken);
        return Ok(publisher);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePublisher(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminPublisherService.DeletePublisherAsync(id, cancellationToken);
        return NoContent();
    }
}
