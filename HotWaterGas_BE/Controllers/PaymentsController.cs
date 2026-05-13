using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly ICheckoutService _checkoutService;

    public PaymentsController(ICheckoutService checkoutService)
    {
        _checkoutService = checkoutService;
    }

    [Authorize]
    [HttpPost("create")]
    public async Task<IActionResult> CreatePayment(
        [FromBody] CreatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SelectedCartItemIds == null || request.SelectedCartItemIds.Count == 0)
        {
            return BadRequest(new { message = "At least one cart item must be selected for payment." });
        }

        var response = await _checkoutService.CreatePaymentAsync(request.SelectedCartItemIds, cancellationToken);
        return Ok(response);
    }

    [HttpGet("return")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymentReturn(
        [FromQuery] string orderCode,
        [FromQuery] string? status,
        [FromQuery] bool success,
        [FromQuery] string? transactionId,
        [FromQuery] decimal? amountPaid,
        CancellationToken cancellationToken = default)
    {
        var response = await _checkoutService.ProcessPaymentReturnAsync(
            orderCode,
            status ?? string.Empty,
            success,
            transactionId,
            amountPaid,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("status/{orderCode}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPaymentStatus(
        [FromRoute] string orderCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return BadRequest(new { message = "Order code is required." });
        }

        var response = await _checkoutService.ProcessPaymentReturnAsync(
            orderCode,
            string.Empty,
            false,
            null,
            null,
            cancellationToken);

        return Ok(response);
    }
}
