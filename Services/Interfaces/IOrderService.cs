using Services.DTOs;

namespace Services.Interfaces;

public interface IOrderService
{
    Task<PagedResponse<MyOrderListItemResponse>> GetMyOrdersAsync(GetMyOrdersRequest request, CancellationToken cancellationToken = default);
    Task<MyOrderDetailResponse?> GetMyOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<AdminOrderDetailResponse?> GetAdminOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default);
}
