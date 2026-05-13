using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/checkout")]
public class CheckoutController : ControllerBase
{
    private readonly ICheckoutService _checkoutService;

    public CheckoutController(ICheckoutService checkoutService)
    {
        _checkoutService = checkoutService;
    }

    [Authorize]
    [HttpPost("preview")]
    public async Task<IActionResult> PreviewCheckout(
        [FromBody] PreviewCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _checkoutService.PreviewCheckoutAsync(request.CartItemIds, cancellationToken);
        return Ok(response);
    }
}

public class PreviewCheckoutRequest
{
    public List<Guid> CartItemIds { get; set; } = new();
}
