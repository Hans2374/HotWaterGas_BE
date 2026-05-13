using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminCategoryService
{
    Task<PagedResponse<CategoryListItemResponse>> GetCategoriesAsync(
        GetAdminCategoriesRequest request,
        CancellationToken cancellationToken = default);

    Task<CategoryDetailResponse?> GetCategoryByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CategoryDetailResponse> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<CategoryDetailResponse> UpdateCategoryAsync(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteCategoryAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
