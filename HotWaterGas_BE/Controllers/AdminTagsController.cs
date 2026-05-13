using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin/tags")]
[Authorize(Roles = "Admin")]
public class AdminTagsController : ControllerBase
{
    private readonly IAdminTagService _adminTagService;

    public AdminTagsController(IAdminTagService adminTagService)
    {
        _adminTagService = adminTagService;
    }

    [HttpGet]
    public async Task<IActionResult> GetTags(
        [FromQuery] GetAdminTagsRequest request,
        CancellationToken cancellationToken = default)
    {
        var pagedResult = await _adminTagService.GetTagsAsync(request, cancellationToken);
        return Ok(pagedResult);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTagById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var tag = await _adminTagService.GetTagByIdAsync(id, cancellationToken);

        if (tag is null)
        {
            return NotFound(new { message = "Tag not found." });
        }

        return Ok(tag);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTag(
        [FromBody] CreateTagRequest request,
        CancellationToken cancellationToken = default)
    {
        var tag = await _adminTagService.CreateTagAsync(request, cancellationToken);
        return Ok(tag);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTag(
        [FromRoute] Guid id,
        [FromBody] UpdateTagRequest request,
        CancellationToken cancellationToken = default)
    {
        var tag = await _adminTagService.UpdateTagAsync(id, request, cancellationToken);
        return Ok(tag);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTag(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminTagService.DeleteTagAsync(id, cancellationToken);
        return NoContent();
    }
}
