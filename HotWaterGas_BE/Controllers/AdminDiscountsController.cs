using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/discounts")]
public class AdminDiscountsController : ControllerBase
{
    private readonly IAdminDiscountService _discountService;

    public AdminDiscountsController(IAdminDiscountService discountService)
    {
        _discountService = discountService;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetDiscounts(CancellationToken cancellationToken = default)
    {
        var response = await _discountService.GetDiscountsAsync(cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDiscountById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await _discountService.GetDiscountByIdAsync(id, cancellationToken);

        if (response is null)
        {
            return NotFound(new { message = "Discount not found." });
        }

        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> CreateDiscount(
        [FromBody] AdminDiscountUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _discountService.CreateDiscountAsync(request, cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDiscount(
        [FromRoute] Guid id,
        [FromBody] AdminDiscountUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _discountService.UpdateDiscountAsync(id, request, cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDiscount(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _discountService.DeleteDiscountAsync(id, cancellationToken);
        return NoContent();
    }
}
