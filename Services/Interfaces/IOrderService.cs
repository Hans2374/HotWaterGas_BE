using Services.DTOs;

namespace Services.Interfaces;

public interface IOrderService
{
    Task<List<MyOrderListItemResponse>> GetMyOrdersAsync(CancellationToken cancellationToken = default);
    Task<MyOrderDetailResponse?> GetMyOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default);
}
