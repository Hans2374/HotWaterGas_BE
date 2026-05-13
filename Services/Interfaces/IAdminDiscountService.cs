using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminDiscountService
{
    Task<List<AdminDiscountListItemResponse>> GetDiscountsAsync(CancellationToken cancellationToken = default);
    Task<AdminDiscountDetailResponse?> GetDiscountByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminDiscountDetailResponse> CreateDiscountAsync(AdminDiscountUpsertRequest request, CancellationToken cancellationToken = default);
    Task<AdminDiscountDetailResponse> UpdateDiscountAsync(Guid id, AdminDiscountUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteDiscountAsync(Guid id, CancellationToken cancellationToken = default);
}
