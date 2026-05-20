namespace Services.DTOs;

public class ProductCatalogQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public Guid? CategoryId { get; set; }
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public List<Guid> TagIds { get; set; } = new();
    public List<string> TagSlugs { get; set; } = new();
}

public class PagedProductCatalogResponse
{
    public List<ProductCatalogItemResponse> Data { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class ProductCatalogItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal FinalPrice { get; set; }
    public decimal? DiscountPrice { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public string PrimaryImageUrl { get; set; } = string.Empty;
    public bool InStock { get; set; }
    public int Stock { get; set; }
}

public class ProductLookupResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class ProductImageResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
}

public class ProductPriceResponse
{
    public decimal BasePrice { get; set; }
    public decimal FinalPrice { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public bool HasDiscount { get; set; }
}

public class ProductRequirementBlockResponse
{
    public string Os { get; set; } = string.Empty;
    public string Processor { get; set; } = string.Empty;
    public string Memory { get; set; } = string.Empty;
    public string Graphics { get; set; } = string.Empty;
    public string Storage { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class ProductSystemRequirementsResponse
{
    public ProductRequirementBlockResponse Minimum { get; set; } = new();
    public ProductRequirementBlockResponse Recommended { get; set; } = new();
}

public class ProductDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public decimal Rating { get; set; }
    public int SoldCount { get; set; }
    public bool HasStock { get; set; }
    public int Stock { get; set; }
    public ProductPriceResponse Price { get; set; } = new();
    public List<ProductImageResponse> Images { get; set; } = new();
    public List<ProductLookupResponse> Categories { get; set; } = new();
    public List<ProductLookupResponse> Tags { get; set; } = new();
    public ProductSystemRequirementsResponse SystemRequirements { get; set; } = new();
}