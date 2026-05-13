using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/wishlist")]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlistService;

    public WishlistController(IWishlistService wishlistService)
    {
        _wishlistService = wishlistService;
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetWishlist(CancellationToken cancellationToken = default)
    {
        var response = await _wishlistService.GetWishlistAsync(cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPost("{productId:guid}")]
    public async Task<IActionResult> AddToWishlist(
        [FromRoute] Guid productId,
        CancellationToken cancellationToken = default)
    {
        var response = await _wishlistService.AddToWishlistAsync(productId, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpDelete("{productId:guid}")]
    public async Task<IActionResult> RemoveFromWishlist(
        [FromRoute] Guid productId,
        CancellationToken cancellationToken = default)
    {
        await _wishlistService.RemoveFromWishlistAsync(productId, cancellationToken);
        return NoContent();
    }
}
