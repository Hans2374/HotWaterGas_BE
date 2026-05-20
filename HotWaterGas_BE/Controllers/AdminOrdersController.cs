using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public AdminOrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// Get full order detail for admin (all orders, not filtered by user).
    /// </summary>
    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrderDetail(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderService.GetAdminOrderDetailAsync(orderId, cancellationToken);
        if (order is null)
        {
            return NotFound(new { message = "Order not found." });
        }
        return Ok(order);
    }
}
