using Services.DTOs;

namespace Services.Interfaces;

public interface IProductCatalogService
{
    Task<PagedProductCatalogResponse> GetProductsAsync(ProductCatalogQuery query, CancellationToken cancellationToken = default);
    Task<List<SearchSuggestionDto>> GetSearchSuggestionsAsync(string? query, CancellationToken cancellationToken = default);
    Task<List<CatalogDirectoryItemResponse>> GetPublishersAsync(CancellationToken cancellationToken = default);
    Task<List<CatalogDirectoryItemResponse>> GetDevelopersAsync(CancellationToken cancellationToken = default);
    Task<CatalogEntityDetailResponse?> GetPublisherByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CatalogEntityDetailResponse?> GetDeveloperByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDetailResponse?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<List<ProductCatalogItemResponse>> GetRecommendationsAsync(Guid productId, int limit, CancellationToken cancellationToken = default);
    Task<List<ProductLookupResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<List<ProductLookupResponse>> GetTagsAsync(CancellationToken cancellationToken = default);
    Task<List<CategoryHomepageResponse>> GetHomepageCategoriesAsync(CancellationToken cancellationToken = default);
    Task<List<FeaturedProductDto>> GetFeaturedProductsAsync(CancellationToken cancellationToken = default);
}