using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductCatalogService _productCatalogService;

    public ProductsController(IProductCatalogService productCatalogService)
    {
        _productCatalogService = productCatalogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? categoryId = null,
        [FromQuery] string? publisherId = null,
        [FromQuery] string? developerId = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        [FromQuery] string? tagIds = null,
        [FromQuery] string? tags = null,
        CancellationToken cancellationToken = default)
    {
        Guid? parsedCategoryId = null;
        if (!string.IsNullOrWhiteSpace(categoryId) && Guid.TryParse(categoryId, out var categoryGuid))
        {
            parsedCategoryId = categoryGuid;
        }

        Guid? parsedPublisherId = null;
        if (!string.IsNullOrWhiteSpace(publisherId) && Guid.TryParse(publisherId, out var publisherGuid))
        {
            parsedPublisherId = publisherGuid;
        }

        Guid? parsedDeveloperId = null;
        if (!string.IsNullOrWhiteSpace(developerId) && Guid.TryParse(developerId, out var developerGuid))
        {
            parsedDeveloperId = developerGuid;
        }

        var query = new ProductCatalogQuery
        {
            Page = page,
            PageSize = pageSize,
            CategoryId = parsedCategoryId,
            PublisherId = parsedPublisherId,
            DeveloperId = parsedDeveloperId,
            Search = search,
            SortBy = sortBy,
            SortDirection = sortDirection,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            TagIds = ParseGuidList(tagIds),
            TagSlugs = ParseStringList(tags)
        };

        var response = await _productCatalogService.GetProductsAsync(query, cancellationToken);
        return Ok(response);
    }

    [HttpGet("search/suggestions")]
    public async Task<IActionResult> GetSearchSuggestions([FromQuery] string? q, CancellationToken cancellationToken = default)
    {
        var suggestions = await _productCatalogService.GetSearchSuggestionsAsync(q, cancellationToken);
        return Ok(suggestions);
    }

    [HttpGet("slug/{slug}")]
    public async Task<IActionResult> GetBySlug([FromRoute] string slug, CancellationToken cancellationToken)
    {
        var product = await _productCatalogService.GetProductBySlugAsync(slug, cancellationToken);
        if (product is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        return Ok(product);
    }

    [HttpGet("{productId:guid}/recommendations")]
    public async Task<IActionResult> GetRecommendations([FromRoute] Guid productId, [FromQuery] int limit = 4, CancellationToken cancellationToken = default)
    {
        var result = await _productCatalogService.GetRecommendationsAsync(productId, limit, cancellationToken);
        return Ok(result);
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeaturedProducts(CancellationToken cancellationToken = default)
    {
        var result = await _productCatalogService.GetFeaturedProductsAsync(cancellationToken);
        return Ok(result);
    }

    private static List<Guid> ParseGuidList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new List<Guid>();
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var guid) ? guid : (Guid?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
    }

    private static List<string> ParseStringList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new List<string>();
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();
    }
}