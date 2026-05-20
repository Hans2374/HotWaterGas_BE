using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class ProductCatalogService : IProductCatalogService
{
    private readonly HotWaterGasDBContext _dbContext;

    public ProductCatalogService(HotWaterGasDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedProductCatalogResponse> GetProductsAsync(ProductCatalogQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : Math.Min(query.PageSize, 100);

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .AsQueryable();

        if (query.CategoryId.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Category.Any(c => c.Id == query.CategoryId.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            productsQuery = productsQuery.Where(p =>
                p.Name.Contains(search) ||
                p.Description.Contains(search) ||
                p.ShortDescription.Contains(search));
        }

        if (query.TagIds.Count > 0)
        {
            productsQuery = productsQuery.Where(p => p.Tag.Any(t => query.TagIds.Contains(t.Id)));
        }

        if (query.TagSlugs.Count > 0)
        {
            productsQuery = productsQuery.Where(p => p.Tag.Any(t => query.TagSlugs.Contains(t.Slug)));
        }

        if (query.MinPrice.HasValue)
        {
            productsQuery = productsQuery.Where(p =>
                (p.Discount != null && p.Discount.StartDate <= now && p.Discount.EndDate >= now
                    ? p.Price * (1 - (p.Discount.Percentage / 100m))
                    : p.Price) >= query.MinPrice.Value);
        }

        if (query.MaxPrice.HasValue)
        {
            productsQuery = productsQuery.Where(p =>
                (p.Discount != null && p.Discount.StartDate <= now && p.Discount.EndDate >= now
                    ? p.Price * (1 - (p.Discount.Percentage / 100m))
                    : p.Price) <= query.MaxPrice.Value);
        }

        productsQuery = ApplySorting(productsQuery, query.SortBy, query.SortDirection);

        var totalItems = await productsQuery.CountAsync(cancellationToken);

        var rows = await productsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Price,
                AvailableKeyCount = p.SteamKeys.Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null),
                DiscountPercentage = p.Discount != null && p.Discount.StartDate <= now && p.Discount.EndDate >= now
                    ? (decimal?)p.Discount.Percentage
                    : null,
                PrimaryImageUrl = p.ProductImages
                    .OrderBy(i => i.IsPrimary ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var mappedItems = rows
            .Select(p =>
            {
                var finalPrice = CalculateFinalPrice(p.Price, p.DiscountPercentage);
                var item = new ProductCatalogItemResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Slug = p.Slug,
                    Price = p.Price,
                    FinalPrice = finalPrice,
                    DiscountPrice = p.DiscountPercentage.HasValue ? finalPrice : null,
                    DiscountPercentage = p.DiscountPercentage,
                    PrimaryImageUrl = p.PrimaryImageUrl ?? string.Empty,
                    Stock = p.AvailableKeyCount,
                    InStock = p.AvailableKeyCount > 0
                };

                return item;
            })
            .ToList();

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        return new PagedProductCatalogResponse
        {
            Data = mappedItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public async Task<ProductDetailResponse?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.ProductImages)
            .Include(p => p.ProductMetadatas)
            .Include(p => p.ProductSystemRequirements)
            .Include(p => p.Category)
            .Include(p => p.Tag)
            .Include(p => p.Reviews)
            .Include(p => p.OrderItems)
            .Include(p => p.Discount)
            .Include(p => p.SteamKeys)
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Slug == slug, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var discountPercentage = product.Discount != null && product.Discount.StartDate <= now && product.Discount.EndDate >= now
            ? (decimal?)product.Discount.Percentage
            : null;

        var basePrice = product.Price;
        var finalPrice = CalculateFinalPrice(basePrice, discountPercentage);

        // Compute actual available stock from Steam keys (canonical source)
        var computedStock = product.SteamKeys
            .Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null);

        var response = new ProductDetailResponse
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Subtitle = product.ProductMetadatas?.Publisher ?? string.Empty,
            Rating = product.Reviews.Count == 0 ? 0 : decimal.Round((decimal)product.Reviews.Average(r => r.Rating), 1),
            SoldCount = product.OrderItems.Sum(oi => oi.Quantity),
            HasStock = computedStock > 0,
            Stock = computedStock,
            Price = new ProductPriceResponse
            {
                BasePrice = basePrice,
                FinalPrice = finalPrice,
                DiscountPercentage = discountPercentage,
                HasDiscount = discountPercentage.HasValue
            },
            Images = product.ProductImages
                .OrderBy(i => i.IsPrimary ? 0 : 1)
                .ThenBy(i => i.DisplayOrder)
                .Select(i => new ProductImageResponse
                {
                    Id = i.Id,
                    Url = i.ImageUrl,
                    IsPrimary = i.IsPrimary,
                    DisplayOrder = i.DisplayOrder
                })
                .ToList(),
            Categories = product.Category
                .Where(c => c.IsActive)
                .Select(c => new ProductLookupResponse
                {
                    Id = c.Id,
                    Name = c.Name,
                    Slug = c.Slug
                })
                .ToList(),
            Tags = product.Tag
                .Where(t => t.IsActive)
                .Select(t => new ProductLookupResponse
                {
                    Id = t.Id,
                    Name = t.Name,
                    Slug = t.Slug
                })
                .ToList(),
            SystemRequirements = new ProductSystemRequirementsResponse
            {
                Minimum = new ProductRequirementBlockResponse
                {
                    Os = product.ProductSystemRequirements?.MinimumOs ?? string.Empty,
                    Processor = product.ProductSystemRequirements?.MinimumProcessor ?? string.Empty,
                    Memory = product.ProductSystemRequirements?.MinimumMemory ?? string.Empty,
                    Graphics = product.ProductSystemRequirements?.MinimumGraphics ?? string.Empty,
                    Storage = product.ProductSystemRequirements?.MinimumStorage ?? string.Empty,
                    Notes = product.ProductSystemRequirements?.MinimumNotes ?? string.Empty
                },
                Recommended = new ProductRequirementBlockResponse
                {
                    Os = product.ProductSystemRequirements?.RecommendedOs ?? string.Empty,
                    Processor = product.ProductSystemRequirements?.RecommendedProcessor ?? string.Empty,
                    Memory = product.ProductSystemRequirements?.RecommendedMemory ?? string.Empty,
                    Graphics = product.ProductSystemRequirements?.RecommendedGraphics ?? string.Empty,
                    Storage = product.ProductSystemRequirements?.RecommendedStorage ?? string.Empty,
                    Notes = product.ProductSystemRequirements?.RecommendedNotes ?? string.Empty
                }
            }
        };

        return response;
    }

    public async Task<List<ProductCatalogItemResponse>> GetRecommendationsAsync(Guid productId, int limit, CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 20);
        var now = DateTime.UtcNow;

        var source = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Tag)
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, cancellationToken);

        if (source is null)
        {
            return new List<ProductCatalogItemResponse>();
        }

        var categoryIds = source.Category.Select(c => c.Id).ToList();
        var tagIds = source.Tag.Select(t => t.Id).ToList();

        var query = _dbContext.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Id != productId)
            .Where(p => p.Category.Any(c => categoryIds.Contains(c.Id)) || p.Tag.Any(t => tagIds.Contains(t.Id)))
            .OrderByDescending(p => p.CreatedAt)
            .Take(safeLimit)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Price,
                AvailableKeyCount = p.SteamKeys.Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null),
                DiscountPercentage = p.Discount != null && p.Discount.StartDate <= now && p.Discount.EndDate >= now
                    ? (decimal?)p.Discount.Percentage
                    : null,
                PrimaryImageUrl = p.ProductImages
                    .OrderBy(i => i.IsPrimary ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault()
            });

        var rows = await query.ToListAsync(cancellationToken);

        return rows.Select(p =>
            {
                var finalPrice = CalculateFinalPrice(p.Price, p.DiscountPercentage);
                return new ProductCatalogItemResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Slug = p.Slug,
                    Price = p.Price,
                    FinalPrice = finalPrice,
                    DiscountPrice = p.DiscountPercentage.HasValue ? finalPrice : null,
                    DiscountPercentage = p.DiscountPercentage,
                    PrimaryImageUrl = p.PrimaryImageUrl ?? string.Empty,
                    Stock = p.AvailableKeyCount,
                    InStock = p.AvailableKeyCount > 0
                };
            })
            .ToList();
    }

    public Task<List<ProductLookupResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new ProductLookupResponse
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug
            })
            .ToListAsync(cancellationToken);
    }

    public Task<List<ProductLookupResponse>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Tags
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new ProductLookupResponse
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug
            })
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Products> ApplySorting(IQueryable<Products> query, string? sortBy, string? sortDirection)
    {
        var sortField = (sortBy ?? "name").Trim().ToLowerInvariant();
        var isDesc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return sortField switch
        {
            "price" => isDesc ? query.OrderByDescending(p => p.Price).ThenBy(p => p.Name) : query.OrderBy(p => p.Price).ThenBy(p => p.Name),
            "releasedate" => isDesc
                ? query.OrderByDescending(p => p.ProductMetadatas.ReleaseDate).ThenBy(p => p.Name)
                : query.OrderBy(p => p.ProductMetadatas.ReleaseDate).ThenBy(p => p.Name),
            _ => isDesc ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name)
        };
    }

    private static decimal CalculateFinalPrice(decimal basePrice, decimal? discountPercentage)
    {
        if (!discountPercentage.HasValue || discountPercentage.Value <= 0)
        {
            return basePrice;
        }

        var discounted = basePrice * (1 - (discountPercentage.Value / 100m));
        return decimal.Round(discounted, 0, MidpointRounding.AwayFromZero);
    }
}