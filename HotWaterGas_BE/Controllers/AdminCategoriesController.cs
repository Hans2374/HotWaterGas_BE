using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin/categories")]
[Authorize(Roles = "Admin")]
public class AdminCategoriesController : ControllerBase
{
    private readonly IAdminCategoryService _adminCategoryService;

    public AdminCategoriesController(IAdminCategoryService adminCategoryService)
    {
        _adminCategoryService = adminCategoryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories(
        [FromQuery] GetAdminCategoriesRequest request,
        CancellationToken cancellationToken = default)
    {
        var pagedResult = await _adminCategoryService.GetCategoriesAsync(request, cancellationToken);
        return Ok(pagedResult);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCategoryById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var category = await _adminCategoryService.GetCategoryByIdAsync(id, cancellationToken);

        if (category is null)
        {
            return NotFound(new { message = "Category not found." });
        }

        return Ok(category);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCategory(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await _adminCategoryService.CreateCategoryAsync(request, cancellationToken);
        return Ok(category);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCategory(
        [FromRoute] Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await _adminCategoryService.UpdateCategoryAsync(id, request, cancellationToken);
        return Ok(category);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCategory(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminCategoryService.DeleteCategoryAsync(id, cancellationToken);
        return NoContent();
    }
}
