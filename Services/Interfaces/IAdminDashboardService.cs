using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<List<RevenueDataPoint>> GetRevenueAsync(string range, CancellationToken cancellationToken = default);
    Task<List<OrderStatusCount>> GetOrderStatusAsync(CancellationToken cancellationToken = default);
    Task<List<LowStockProductItem>> GetLowStockProductsAsync(int threshold, CancellationToken cancellationToken = default);
    Task<InventoryAlertsResponse> GetInventoryAlertsAsync(int threshold, CancellationToken cancellationToken = default);
    Task<PagedResponse<RecentOrderItem>> GetRecentOrdersAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
