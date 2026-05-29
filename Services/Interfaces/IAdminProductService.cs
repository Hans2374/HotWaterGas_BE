using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminProductService
{
    Task<PagedAdminProductListResponse> GetProductsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedAdminProductListResponse> GetFilteredProductsAsync(AdminProductQueryRequest query, CancellationToken cancellationToken = default);
    Task<PagedAdminProductListResponse> GetDeletedProductsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminProductDetailResponse?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminProductDetailResponse> CreateProductAsync(AdminProductUpsertRequest request, CancellationToken cancellationToken = default);
    Task<AdminProductDetailResponse> UpdateProductAsync(Guid id, AdminProductUpsertRequest request, CancellationToken cancellationToken = default);
    Task DisableProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task RestoreProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task HardDeleteProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<FeaturedProductAdminDto>> GetFeaturedProductsAsync(CancellationToken cancellationToken = default);
    Task<List<FeaturedProductAdminDto>> UpdateFeaturedProductsAsync(UpdateFeaturedProductsRequest request, CancellationToken cancellationToken = default);
}
