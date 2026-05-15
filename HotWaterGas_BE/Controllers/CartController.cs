using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/cart")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetMyCart(CancellationToken cancellationToken = default)
    {
        var response = await _cartService.GetMyCartAsync(cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AddToCart(
        [FromBody] AddToCartRequest request,
        CancellationToken cancellationToken = default)
    {
        // Returns full CartResponse for frontend state consistency
        var response = await _cartService.AddToCartAsync(request.ProductId, request.Quantity, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPut("{productId:guid}/quantity")]
    public async Task<IActionResult> UpdateCartItemQuantity(
        [FromRoute] Guid productId,
        [FromBody] UpdateCartQuantityRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _cartService.UpdateCartItemQuantityAsync(productId, request.Quantity, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpDelete("{productId:guid}")]
    public async Task<IActionResult> RemoveFromCart(
        [FromRoute] Guid productId,
        CancellationToken cancellationToken = default)
    {
        await _cartService.RemoveFromCartAsync(productId, cancellationToken);
        return NoContent();
    }
}
