using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/products")]
public class AdminProductsController : ControllerBase
{
    private readonly IAdminProductService _adminProductService;
    private readonly ISteamKeyService _steamKeyService;

    public AdminProductsController(IAdminProductService adminProductService, ISteamKeyService steamKeyService)
    {
        _adminProductService = adminProductService;
        _steamKeyService = steamKeyService;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new AdminProductQueryRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search
        };

        if (!string.IsNullOrWhiteSpace(categoryId) && Guid.TryParse(categoryId, out var categoryGuid))
        {
            query.CategoryId = categoryGuid;
        }

        var response = await _adminProductService.GetFilteredProductsAsync(query, cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id:guid}/disable")]
    public async Task<IActionResult> DisableProduct(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminProductService.DisableProductAsync(id, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> RestoreProduct(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminProductService.RestoreProductAsync(id, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}/hard-delete")]
    public async Task<IActionResult> HardDeleteProduct(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminProductService.HardDeleteProductAsync(id, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProductById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await _adminProductService.GetProductByIdAsync(id, cancellationToken);

        if (response is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> CreateProduct(
        [FromBody] AdminProductUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _adminProductService.CreateProductAsync(request, cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProduct(
        [FromRoute] Guid id,
        [FromBody] AdminProductUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _adminProductService.UpdateProductAsync(id, request, cancellationToken);
        return Ok(response);
    }

    // ─── Steam Key Management ──────────────────────────────────────────────────

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}/keys/summary")]
    public async Task<IActionResult> GetSteamKeySummary(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var summary = await _steamKeyService.GetSummaryAsync(id, cancellationToken);
        return Ok(summary);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}/keys")]
    public async Task<IActionResult> GetSteamKeys(
        [FromRoute] Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _steamKeyService.GetKeysAsync(id, page, pageSize, status, cancellationToken);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}/keys/{keyId:guid}")]
    public async Task<IActionResult> GetSteamKeyById(
        [FromRoute] Guid id,
        [FromRoute] Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var key = await _steamKeyService.GetKeyByIdAsync(id, keyId, cancellationToken);

        if (key == null)
        {
            return NotFound(new { message = "Steam key not found." });
        }

        return Ok(key);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/keys")]
    public async Task<IActionResult> BulkUploadSteamKeys(
        [FromRoute] Guid id,
        [FromBody] SteamKeyBulkUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _steamKeyService.BulkUploadAsync(id, request.KeyValues, cancellationToken);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}/keys/{keyId:guid}")]
    public async Task<IActionResult> UpdateSteamKey(
        [FromRoute] Guid id,
        [FromRoute] Guid keyId,
        [FromBody] SteamKeyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _steamKeyService.UpdateKeyAsync(id, keyId, request.KeyValue, cancellationToken);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id:guid}/keys/{keyId:guid}/disable")]
    public async Task<IActionResult> DisableSteamKey(
        [FromRoute] Guid id,
        [FromRoute] Guid keyId,
        CancellationToken cancellationToken = default)
    {
        await _steamKeyService.DisableKeyAsync(id, keyId, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id:guid}/keys/{keyId:guid}/enable")]
    public async Task<IActionResult> EnableSteamKey(
        [FromRoute] Guid id,
        [FromRoute] Guid keyId,
        CancellationToken cancellationToken = default)
    {
        await _steamKeyService.EnableKeyAsync(id, keyId, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}/keys/{keyId:guid}")]
    public async Task<IActionResult> DeleteSteamKey(
        [FromRoute] Guid id,
        [FromRoute] Guid keyId,
        CancellationToken cancellationToken = default)
    {
        await _steamKeyService.DeleteKeyAsync(id, keyId, cancellationToken);
        return NoContent();
    }
}
