using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IOrderService _orderService;

    public MeController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var request = new GetMyOrdersRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var response = await _orderService.GetMyOrdersAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("orders/{orderId:guid}")]
    public async Task<IActionResult> GetMyOrderDetail(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var response = await _orderService.GetMyOrderDetailAsync(orderId, cancellationToken);

        if (response is null)
        {
            return NotFound(new { message = "Order not found." });
        }

        return Ok(response);
    }
}
