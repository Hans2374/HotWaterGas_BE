using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _dashboardService;

    public AdminDashboardController(IAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// KPI summary cards: revenue, order counts, low stock, customers.
    /// </summary>
    [HttpGet("dashboard/summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetSummaryAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Revenue trend chart data.
    /// Query params: range=7d (default), 30d, or 12m.
    /// </summary>
    [HttpGet("dashboard/revenue")]
    public async Task<IActionResult> GetRevenue(
        [FromQuery] string range = "7d",
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetRevenueAsync(range, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Order status distribution for chart.
    /// </summary>
    [HttpGet("dashboard/order-status")]
    public async Task<IActionResult> GetOrderStatus(CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetOrderStatusAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Products at risk of stockout.
    /// Query params: threshold=N (default 5, max any value).
    /// </summary>
    [HttpGet("dashboard/low-stock")]
    public async Task<IActionResult> GetLowStock(
        [FromQuery] int threshold = 5,
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetLowStockProductsAsync(threshold, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Inventory alerts: out-of-stock and low-stock products.
    /// Query params: threshold=N (default 5).
    /// </summary>
    [HttpGet("dashboard/inventory-alerts")]
    public async Task<IActionResult> GetInventoryAlerts(
        [FromQuery] int threshold = 5,
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetInventoryAlertsAsync(threshold, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Paginated recent orders for dashboard table.
    /// Query params: page (default 1), pageSize (default 10, max 50).
    /// </summary>
    [HttpGet("orders/recent")]
    public async Task<IActionResult> GetRecentOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetRecentOrdersAsync(page, pageSize, cancellationToken);
        return Ok(result);
    }
}
