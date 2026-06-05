using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminProductService : IAdminProductService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ILogger<AdminProductService>? _logger;

    public AdminProductService(HotWaterGasDBContext dbContext, ILogger<AdminProductService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedAdminProductListResponse> GetProductsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = new AdminProductQueryRequest
        {
            Page = page,
            PageSize = pageSize
        };
        return await GetFilteredProductsAsync(query, cancellationToken);
    }

    public async Task<PagedAdminProductListResponse> GetFilteredProductsAsync(AdminProductQueryRequest query, CancellationToken cancellationToken = default)
    {
        var safePage = query.Page < 1 ? 1 : query.Page;
        var safePageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 100);

        var now = DateTime.UtcNow;

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .Include(p => p.ProductImages)
            .Include(p => p.Discount)
            .Include(p => p.ProductMetadatas)
            .Include(p => p.Publisher)
            .Include(p => p.Developer)
            .Include(p => p.Category)
            .Include(p => p.SteamKeys)
            .AsQueryable();

        // Return ALL products (both active and deleted) - admin unified view
        // No automatic IsDeleted exclusion anymore

        // Search filter - searches across Name, Slug, Developer, and Publisher
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchTerm = query.Search.Trim().ToLower();

            productsQuery = productsQuery.Where(p =>
                (p.Name != null && EF.Functions.Like(p.Name.ToLower(), $"%{searchTerm}%")) ||
                (p.Slug != null && EF.Functions.Like(p.Slug.ToLower(), $"%{searchTerm}%")) ||
                (p.Publisher != null && p.Publisher.Name != null && EF.Functions.Like(p.Publisher.Name.ToLower(), $"%{searchTerm}%")) ||
                (p.Developer != null && p.Developer.Name != null && EF.Functions.Like(p.Developer.Name.ToLower(), $"%{searchTerm}%")) ||
                (p.ProductMetadatas != null &&
                    ((p.ProductMetadatas.Developer != null && EF.Functions.Like(p.ProductMetadatas.Developer.ToLower(), $"%{searchTerm}%")) ||
                     (p.ProductMetadatas.Publisher != null && EF.Functions.Like(p.ProductMetadatas.Publisher.ToLower(), $"%{searchTerm}%")))));

            _logger?.LogInformation(
                "[AdminProducts.Search] Search={Search} TermNormalized={Normalized}",
                query.Search, searchTerm);
        }

        // Category filter
        if (query.CategoryId.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Category.Any(c => c.Id == query.CategoryId.Value));
        }

        // Pre-compute available key counts (only truly available keys)
        var productsWithCounts = productsQuery
            .Select(p => new
            {
                Product = p,
                AvailableKeyCount = p.SteamKeys.Count(k => k.Status == 0 && k.OrderId == null && k.InvalidatedAt == null)
            })
            .ToList();

        var totalItems = productsWithCounts.Count;

        // Apply pagination after filtering
        var pagedProducts = productsWithCounts
            .OrderByDescending(x => x.Product.UpdatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        var products = pagedProducts
            .Select(x => new AdminProductListItemResponse
            {
                Id = x.Product.Id,
                Name = x.Product.Name,
                Slug = x.Product.Slug,
                DeveloperName = x.Product.Developer?.Name ?? x.Product.ProductMetadatas?.Developer ?? string.Empty,
                PublisherName = x.Product.Publisher?.Name ?? x.Product.ProductMetadatas?.Publisher ?? string.Empty,
                Subtitle = string.Join(" - ", new[]
                {
                    x.Product.Developer?.Name ?? x.Product.ProductMetadatas?.Developer,
                    x.Product.Publisher?.Name ?? x.Product.ProductMetadatas?.Publisher
                }.Where(value => !string.IsNullOrWhiteSpace(value))),
                Price = x.Product.Price,
                FinalPrice = x.Product.Discount != null && x.Product.Discount.StartDate <= now && x.Product.Discount.EndDate >= now
                    ? Math.Round(x.Product.Price * (1 - (x.Product.Discount.Percentage / 100m)), 0, MidpointRounding.AwayFromZero)
                    : x.Product.Price,
                DiscountPrice = x.Product.Discount != null && x.Product.Discount.StartDate <= now && x.Product.Discount.EndDate >= now
                    ? Math.Round(x.Product.Price * (1 - (x.Product.Discount.Percentage / 100m)), 0, MidpointRounding.AwayFromZero)
                    : null,
                HasDiscount = x.Product.Discount != null && x.Product.Discount.StartDate <= now && x.Product.Discount.EndDate >= now,
                // Stock is now derived from actual available keys (canonical source)
                Stock = x.AvailableKeyCount,
                AvailableSteamKeyCount = x.AvailableKeyCount,
                IsDeleted = x.Product.IsDeleted,
                UpdatedAt = x.Product.UpdatedAt,
                PrimaryImageUrl = x.Product.ProductImages
                    .OrderBy(i => i.IsPrimary ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault() ?? string.Empty
            })
            .ToList();

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)safePageSize);

        _logger?.LogInformation(
            "[AdminProducts.Search.Results] Search={Search} CategoryId={CategoryId} TotalItems={TotalItems}",
            query.Search, query.CategoryId, totalItems);

        return new PagedAdminProductListResponse
        {
            Data = products,
            Page = safePage,
            PageSize = safePageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public async Task<PagedAdminProductListResponse> GetDeletedProductsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);

        var now = DateTime.UtcNow;

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .Include(p => p.ProductImages)
            .Include(p => p.Discount)
            .Include(p => p.ProductMetadatas)
            .Include(p => p.Publisher)
            .Include(p => p.Developer)
            .Include(p => p.SteamKeys)
            .Where(p => p.IsDeleted)
            .AsQueryable();

        var totalItems = await productsQuery.CountAsync(cancellationToken);

        var products = await productsQuery
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(p => new
            {
                Product = p,
                AvailableKeyCount = p.SteamKeys.Count(k => k.Status == 0 && k.OrderId == null && k.InvalidatedAt == null)
            })
            .ToListAsync(cancellationToken);

        var responseProducts = products
            .Select(x => new AdminProductListItemResponse
            {
                Id = x.Product.Id,
                Name = x.Product.Name,
                Slug = x.Product.Slug,
                DeveloperName = x.Product.Developer?.Name ?? x.Product.ProductMetadatas?.Developer ?? string.Empty,
                PublisherName = x.Product.Publisher?.Name ?? x.Product.ProductMetadatas?.Publisher ?? string.Empty,
                Subtitle = string.Join(" - ", new[]
                {
                    x.Product.Developer?.Name ?? x.Product.ProductMetadatas?.Developer,
                    x.Product.Publisher?.Name ?? x.Product.ProductMetadatas?.Publisher
                }.Where(value => !string.IsNullOrWhiteSpace(value))),
                Price = x.Product.Price,
                FinalPrice = x.Product.Discount != null && x.Product.Discount.StartDate <= now && x.Product.Discount.EndDate >= now
                    ? Math.Round(x.Product.Price * (1 - (x.Product.Discount.Percentage / 100m)), 0, MidpointRounding.AwayFromZero)
                    : x.Product.Price,
                DiscountPrice = x.Product.Discount != null && x.Product.Discount.StartDate <= now && x.Product.Discount.EndDate >= now
                    ? Math.Round(x.Product.Price * (1 - (x.Product.Discount.Percentage / 100m)), 0, MidpointRounding.AwayFromZero)
                    : null,
                HasDiscount = x.Product.Discount != null && x.Product.Discount.StartDate <= now && x.Product.Discount.EndDate >= now,
                // Stock is now derived from actual available keys (canonical source)
                Stock = x.AvailableKeyCount,
                AvailableSteamKeyCount = x.AvailableKeyCount,
                IsDeleted = x.Product.IsDeleted,
                UpdatedAt = x.Product.UpdatedAt,
                PrimaryImageUrl = x.Product.ProductImages
                    .OrderBy(i => i.IsPrimary ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault() ?? string.Empty
            })
            .ToList();

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)safePageSize);

        return new PagedAdminProductListResponse
        {
            Data = responseProducts,
            Page = safePage,
            PageSize = safePageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public async Task<AdminProductDetailResponse?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.ProductImages)
            .Include(p => p.ProductMetadatas)
            .Include(p => p.ProductSystemRequirements)
            .Include(p => p.Category)
            .Include(p => p.Tag)
            .Include(p => p.Discount)
            .Include(p => p.Publisher)
            .Include(p => p.Developer)
            .Include(p => p.SteamKeys)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var discountPercentage = product.Discount != null
            ? (decimal?)product.Discount.Percentage
            : null;

        // Compute actual available stock from Steam keys (canonical source)
        var computedStock = product.SteamKeys
            .Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null);

        return new AdminProductDetailResponse
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            Description = product.Description,
            ShortDescription = product.ShortDescription,
            Price = product.Price,
            DiscountPercentage = discountPercentage,
            DiscountId = product.Discount?.Id,
            Stock = computedStock,
            PublisherId = product.PublisherId,
            DeveloperId = product.DeveloperId,
            Metadata = new AdminProductMetadataResponse
            {
                Publisher = product.ProductMetadatas?.Publisher ?? string.Empty,
                Developer = product.ProductMetadatas?.Developer ?? string.Empty,
                ReleaseDate = product.ProductMetadatas?.ReleaseDate,
                Platform = product.ProductMetadatas?.Platform ?? string.Empty,
                PublisherId = product.PublisherId,
                DeveloperId = product.DeveloperId
            },
            SystemRequirements = new AdminProductSystemRequirementsResponse
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
            },
            Images = product.ProductImages
                .OrderBy(i => i.IsPrimary ? 0 : 1)
                .ThenBy(i => i.DisplayOrder)
                .Select(i => new AdminProductImageResponse
                {
                    Id = i.Id,
                    Url = i.ImageUrl
                })
                .ToList(),
            Categories = product.Category
                .Select(c => new ProductLookupResponse
                {
                    Id = c.Id,
                    Name = c.Name,
                    Slug = c.Slug
                })
                .ToList(),
            Tags = product.Tag
                .Select(t => new ProductLookupResponse
                {
                    Id = t.Id,
                    Name = t.Name,
                    Slug = t.Slug
                })
                .ToList()
        };
    }

    public async Task<AdminProductDetailResponse> CreateProductAsync(AdminProductUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var slug = request.Slug ?? GenerateSlug(request.Name);

        var product = new Products
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = slug,
            Description = request.Description,
            ShortDescription = request.ShortDescription,
            Price = request.Price,
            Stock = 0,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            PublisherId = request.PublisherId,
            DeveloperId = request.DeveloperId
        };

        _dbContext.Products.Add(product);

        if (request.Metadata != null)
        {
            var metadata = new ProductMetadatas
            {
                ProductId = product.Id,
                Publisher = request.Metadata.Publisher ?? string.Empty,
                Developer = request.Metadata.Developer ?? string.Empty,
                ReleaseDate = NormalizeToUtc(request.Metadata.ReleaseDate),
                Platform = request.Metadata.Platform ?? string.Empty
            };
            _dbContext.ProductMetadatas.Add(metadata);
        }

        if (request.SystemRequirements != null)
        {
            var sysReq = new ProductSystemRequirements
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                MinimumOs = request.SystemRequirements.Minimum?.Os ?? string.Empty,
                MinimumProcessor = request.SystemRequirements.Minimum?.Processor ?? string.Empty,
                MinimumMemory = request.SystemRequirements.Minimum?.Memory ?? string.Empty,
                MinimumGraphics = request.SystemRequirements.Minimum?.Graphics ?? string.Empty,
                MinimumStorage = request.SystemRequirements.Minimum?.Storage ?? string.Empty,
                MinimumNotes = request.SystemRequirements.Minimum?.Notes ?? string.Empty,
                RecommendedOs = request.SystemRequirements.Recommended?.Os ?? string.Empty,
                RecommendedProcessor = request.SystemRequirements.Recommended?.Processor ?? string.Empty,
                RecommendedMemory = request.SystemRequirements.Recommended?.Memory ?? string.Empty,
                RecommendedGraphics = request.SystemRequirements.Recommended?.Graphics ?? string.Empty,
                RecommendedStorage = request.SystemRequirements.Recommended?.Storage ?? string.Empty,
                RecommendedNotes = request.SystemRequirements.Recommended?.Notes ?? string.Empty
            };
            _dbContext.ProductSystemRequirements.Add(sysReq);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var imageUrls = request.Images?.Select(i => i.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList()
            ?? request.ImageUrls?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList()
            ?? new List<string>();

        for (int i = 0; i < imageUrls.Count; i++)
        {
            var image = new ProductImages
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ImageUrl = imageUrls[i],
                IsPrimary = i == 0,
                DisplayOrder = i
            };
            _dbContext.ProductImages.Add(image);
        }

        if (request.CategoryIds?.Count > 0 || request.CategoryId.HasValue)
        {
            var idsToAdd = request.CategoryIds?.Count > 0
                ? request.CategoryIds
                : new List<Guid> { request.CategoryId!.Value };

            var categories = await _dbContext.Categories
                .Where(c => idsToAdd.Contains(c.Id))
                .ToListAsync(cancellationToken);
            
            foreach (var category in categories)
            {
                product.Category.Add(category);
            }
        }

        if (request.TagIds?.Count > 0)
        {
            var tags = await _dbContext.Tags
                .Where(t => request.TagIds.Contains(t.Id))
                .ToListAsync(cancellationToken);
            
            foreach (var tag in tags)
            {
                product.Tag.Add(tag);
            }
        }

        // Link discount by DiscountId
        if (request.DiscountId.HasValue)
        {
            var discountExists = await _dbContext.Discounts
                .AnyAsync(d => d.Id == request.DiscountId.Value, cancellationToken);

            if (discountExists)
            {
                product.DiscountId = request.DiscountId.Value;
            }
        }
        else if (request.DiscountPercentage.HasValue && request.DiscountPercentage.Value > 0)
        {
            // Fallback: create a placeholder discount if only percentage is provided
            var placeholderDiscount = new Discounts
            {
                Id = Guid.NewGuid(),
                Percentage = request.DiscountPercentage.Value,
                StartDate = now,
                EndDate = now.AddDays(365)
            };
            _dbContext.Discounts.Add(placeholderDiscount);
            product.DiscountId = placeholderDiscount.Id;
        }
        else
        {
            product.DiscountId = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetProductByIdAsync(product.Id, cancellationToken))!;
    }

    public async Task<AdminProductDetailResponse> UpdateProductAsync(Guid id, AdminProductUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var product = await _dbContext.Products
            .Include(p => p.ProductImages)
            .Include(p => p.ProductMetadatas)
            .Include(p => p.ProductSystemRequirements)
            .Include(p => p.Category)
            .Include(p => p.Tag)
            .Include(p => p.Discount)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Product not found.");
        }

        product.Name = request.Name;
        product.Slug = request.Slug ?? product.Slug;
        product.Description = request.Description;
        product.ShortDescription = request.ShortDescription;
        product.Price = request.Price;
        product.UpdatedAt = now;
        product.PublisherId = request.PublisherId;
        product.DeveloperId = request.DeveloperId;

        if (product.ProductMetadatas != null && request.Metadata != null)
        {
            product.ProductMetadatas.Publisher = request.Metadata.Publisher ?? product.ProductMetadatas.Publisher;
            product.ProductMetadatas.Developer = request.Metadata.Developer ?? product.ProductMetadatas.Developer;
            product.ProductMetadatas.ReleaseDate = NormalizeToUtc(request.Metadata.ReleaseDate ?? product.ProductMetadatas.ReleaseDate);
            product.ProductMetadatas.Platform = request.Metadata.Platform ?? product.ProductMetadatas.Platform;
        }
        else if (request.Metadata != null)
        {
            var metadata = new ProductMetadatas
            {
                ProductId = product.Id,
                Publisher = request.Metadata.Publisher ?? string.Empty,
                Developer = request.Metadata.Developer ?? string.Empty,
                ReleaseDate = NormalizeToUtc(request.Metadata.ReleaseDate),
                Platform = request.Metadata.Platform ?? string.Empty
            };
            _dbContext.ProductMetadatas.Add(metadata);
        }

        if (product.ProductSystemRequirements != null && request.SystemRequirements != null)
        {
            product.ProductSystemRequirements.MinimumOs = request.SystemRequirements.Minimum?.Os ?? product.ProductSystemRequirements.MinimumOs;
            product.ProductSystemRequirements.MinimumProcessor = request.SystemRequirements.Minimum?.Processor ?? product.ProductSystemRequirements.MinimumProcessor;
            product.ProductSystemRequirements.MinimumMemory = request.SystemRequirements.Minimum?.Memory ?? product.ProductSystemRequirements.MinimumMemory;
            product.ProductSystemRequirements.MinimumGraphics = request.SystemRequirements.Minimum?.Graphics ?? product.ProductSystemRequirements.MinimumGraphics;
            product.ProductSystemRequirements.MinimumStorage = request.SystemRequirements.Minimum?.Storage ?? product.ProductSystemRequirements.MinimumStorage;
            product.ProductSystemRequirements.MinimumNotes = request.SystemRequirements.Minimum?.Notes ?? product.ProductSystemRequirements.MinimumNotes;
            product.ProductSystemRequirements.RecommendedOs = request.SystemRequirements.Recommended?.Os ?? product.ProductSystemRequirements.RecommendedOs;
            product.ProductSystemRequirements.RecommendedProcessor = request.SystemRequirements.Recommended?.Processor ?? product.ProductSystemRequirements.RecommendedProcessor;
            product.ProductSystemRequirements.RecommendedMemory = request.SystemRequirements.Recommended?.Memory ?? product.ProductSystemRequirements.RecommendedMemory;
            product.ProductSystemRequirements.RecommendedGraphics = request.SystemRequirements.Recommended?.Graphics ?? product.ProductSystemRequirements.RecommendedGraphics;
            product.ProductSystemRequirements.RecommendedStorage = request.SystemRequirements.Recommended?.Storage ?? product.ProductSystemRequirements.RecommendedStorage;
            product.ProductSystemRequirements.RecommendedNotes = request.SystemRequirements.Recommended?.Notes ?? product.ProductSystemRequirements.RecommendedNotes;
        }
        else if (request.SystemRequirements != null)
        {
            var sysReq = new ProductSystemRequirements
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                MinimumOs = request.SystemRequirements.Minimum?.Os ?? string.Empty,
                MinimumProcessor = request.SystemRequirements.Minimum?.Processor ?? string.Empty,
                MinimumMemory = request.SystemRequirements.Minimum?.Memory ?? string.Empty,
                MinimumGraphics = request.SystemRequirements.Minimum?.Graphics ?? string.Empty,
                MinimumStorage = request.SystemRequirements.Minimum?.Storage ?? string.Empty,
                MinimumNotes = request.SystemRequirements.Minimum?.Notes ?? string.Empty,
                RecommendedOs = request.SystemRequirements.Recommended?.Os ?? string.Empty,
                RecommendedProcessor = request.SystemRequirements.Recommended?.Processor ?? string.Empty,
                RecommendedMemory = request.SystemRequirements.Recommended?.Memory ?? string.Empty,
                RecommendedGraphics = request.SystemRequirements.Recommended?.Graphics ?? string.Empty,
                RecommendedStorage = request.SystemRequirements.Recommended?.Storage ?? string.Empty,
                RecommendedNotes = request.SystemRequirements.Recommended?.Notes ?? string.Empty
            };
            _dbContext.ProductSystemRequirements.Add(sysReq);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (request.Images != null || (request.ImageUrls != null && request.ImageUrls.Count > 0))
        {
            _dbContext.ProductImages.RemoveRange(product.ProductImages);

            var imageUrls = request.Images?.Select(i => i.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList()
                ?? request.ImageUrls?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList()
                ?? new List<string>();

            for (int i = 0; i < imageUrls.Count; i++)
            {
                var image = new ProductImages
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    ImageUrl = imageUrls[i],
                    IsPrimary = i == 0,
                    DisplayOrder = i
                };
                _dbContext.ProductImages.Add(image);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (request.CategoryIds != null || request.CategoryId.HasValue)
        {
            product.Category.Clear();

            var idsToUpdate = request.CategoryIds?.Count > 0
                ? request.CategoryIds
                : request.CategoryId.HasValue
                    ? new List<Guid> { request.CategoryId.Value }
                    : new List<Guid>();

            if (idsToUpdate.Count > 0)
            {
                var categories = await _dbContext.Categories
                    .Where(c => idsToUpdate.Contains(c.Id))
                    .ToListAsync(cancellationToken);
                
                foreach (var category in categories)
                {
                    product.Category.Add(category);
                }
            }
        }

        if (request.TagIds != null)
        {
            product.Tag.Clear();
            
            if (request.TagIds.Count > 0)
            {
                var tags = await _dbContext.Tags
                    .Where(t => request.TagIds.Contains(t.Id))
                    .ToListAsync(cancellationToken);
                
                foreach (var tag in tags)
                {
                    product.Tag.Add(tag);
                }
            }
        }

        // Update discount link
        if (request.DiscountId.HasValue)
        {
            var discountExists = await _dbContext.Discounts
                .AnyAsync(d => d.Id == request.DiscountId.Value, cancellationToken);

            product.DiscountId = discountExists ? request.DiscountId.Value : null;
        }
        else if (request.DiscountPercentage.HasValue && request.DiscountPercentage.Value > 0)
        {
            if (product.DiscountId.HasValue)
            {
                var existingDiscount = await _dbContext.Discounts
                    .FirstOrDefaultAsync(d => d.Id == product.DiscountId.Value, cancellationToken);
                if (existingDiscount != null)
                {
                    existingDiscount.Percentage = request.DiscountPercentage.Value;
                }
                else
                {
                    var newDiscount = new Discounts
                    {
                        Id = Guid.NewGuid(),
                        Percentage = request.DiscountPercentage.Value,
                        StartDate = now,
                        EndDate = now.AddDays(365)
                    };
                    _dbContext.Discounts.Add(newDiscount);
                    product.DiscountId = newDiscount.Id;
                }
            }
            else
            {
                var newDiscount = new Discounts
                {
                    Id = Guid.NewGuid(),
                    Percentage = request.DiscountPercentage.Value,
                    StartDate = now,
                    EndDate = now.AddDays(365)
                };
                _dbContext.Discounts.Add(newDiscount);
                product.DiscountId = newDiscount.Id;
            }
        }
        else
        {
            product.DiscountId = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetProductByIdAsync(product.Id, cancellationToken))!;
    }

    public async Task DisableProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Product not found.");
        }

        var productName = product.Name;
        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminProduct.Disable] ProductId={ProductId}, ProductName={ProductName}, Action=Disabled",
            id, productName);
    }

    public async Task RestoreProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Product not found.");
        }

        var productName = product.Name;
        product.IsDeleted = false;
        product.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminProduct.Restore] ProductId={ProductId}, ProductName={ProductName}, Action=Restored",
            id, productName);
    }

    public async Task HardDeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .Include(p => p.ProductImages)
            .Include(p => p.ProductMetadatas)
            .Include(p => p.ProductSystemRequirements)
            .Include(p => p.CartItems)
            .Include(p => p.OrderItems)
            .Include(p => p.Reviews)
            .Include(p => p.SteamKeys)
            .Include(p => p.WishlistItems)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            _logger?.LogWarning("[Product.HardDelete] Product not found ProductId={ProductId}", id);
            throw new KeyNotFoundException("Product not found.");
        }

        var productName = product.Name;
        var steamKeyCount = product.SteamKeys?.Count ?? 0;

        _logger?.LogInformation(
            "[Product.HardDelete] Starting deletion ProductId={ProductId} ProductName={ProductName} SteamKeyCount={SteamKeyCount}",
            id, productName, steamKeyCount);

        // Use explicit transaction for atomicity
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Phase 1: Delete Steam Keys (most critical for inventory integrity)
            if (steamKeyCount > 0)
            {
                _dbContext.SteamKeys.RemoveRange(product.SteamKeys);
                _logger?.LogInformation("[Product.HardDelete] Phase1 SteamKeys marked for deletion Count={Count}", steamKeyCount);
            }

            // Phase 2: Delete other dependent entities
            _dbContext.ProductImages.RemoveRange(product.ProductImages);
            _dbContext.CartItems.RemoveRange(product.CartItems);
            _dbContext.OrderItems.RemoveRange(product.OrderItems);
            _dbContext.Reviews.RemoveRange(product.Reviews);
            _dbContext.WishlistItems.RemoveRange(product.WishlistItems);

            // Phase 3: Delete related single entities (if they exist as separate tables)
            if (product.ProductMetadatas != null)
            {
                _dbContext.ProductMetadatas.Remove(product.ProductMetadatas);
            }
            if (product.ProductSystemRequirements != null)
            {
                _dbContext.ProductSystemRequirements.Remove(product.ProductSystemRequirements);
            }

            // Phase 4: Delete the product itself
            _dbContext.Products.Remove(product);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger?.LogWarning(
                "[Product.HardDelete] Completed ProductId={ProductId} ProductName={ProductName} SteamKeyCount={SteamKeyCount}",
                id, productName, steamKeyCount);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogError(ex,
                "[Product.HardDelete] Failed ProductId={ProductId} ProductName={ProductName}",
                id, productName);
            throw;
        }
    }

    public async Task<List<FeaturedProductAdminDto>> GetFeaturedProductsAsync(CancellationToken cancellationToken = default)
    {
        var featured = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.IsFeatured)
            .OrderBy(p => p.Name)
            .Select(p => new FeaturedProductAdminDto
            {
                Id = p.Id,
                Name = p.Name,
                PrimaryImageUrl = p.ProductImages
                    .OrderBy(i => i.IsPrimary ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault() ?? string.Empty,
                IsFeatured = true
            })
            .ToListAsync(cancellationToken);

        _logger?.LogInformation("[AdminProduct.GetFeatured] Count={Count}", featured.Count);
        return featured;
    }

    public async Task<List<FeaturedProductAdminDto>> UpdateFeaturedProductsAsync(UpdateFeaturedProductsRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ApiException(400, "Request body is required.");
        }

        if (request.ProductIds == null)
        {
            throw new ApiException(400, "ProductIds list is required.");
        }

        var distinctIds = request.ProductIds.Distinct().ToList();

        if (distinctIds.Count != request.ProductIds.Count)
        {
            throw new ApiException(400, "Duplicate product IDs are not allowed.");
        }

        if (distinctIds.Count > 5)
        {
            throw new ApiException(400, "A maximum of 5 featured products is allowed.");
        }

        if (distinctIds.Count > 0)
        {
            var existingCount = await _dbContext.Products
                .CountAsync(p => distinctIds.Contains(p.Id), cancellationToken);

            if (existingCount != distinctIds.Count)
            {
                throw new ApiException(400, "One or more product IDs do not exist.");
            }
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var now = DateTime.UtcNow;

            _dbContext.Products
                .Where(p => p.IsFeatured)
                .ExecuteUpdate(setters => setters
                    .SetProperty(p => p.IsFeatured, false)
                    .SetProperty(p => p.UpdatedAt, now));

            if (distinctIds.Count > 0)
            {
                _dbContext.Products
                    .Where(p => distinctIds.Contains(p.Id))
                    .ExecuteUpdate(setters => setters
                        .SetProperty(p => p.IsFeatured, true)
                        .SetProperty(p => p.UpdatedAt, now));
            }

            await transaction.CommitAsync(cancellationToken);

            _logger?.LogInformation(
                "[AdminProduct.UpdateFeatured] FeaturedCount={Count} ProductIds={ProductIds}",
                distinctIds.Count,
                string.Join(",", distinctIds));

            return await GetFeaturedProductsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogError(ex, "[AdminProduct.UpdateFeatured] Failed");
            throw;
        }
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "")
            .Replace("&", "and")
            .Replace("?", "")
            .Replace("!", "")
            .Replace(",", "")
            .Replace(".", "")
            .Replace(":", "")
            .Replace(";", "")
            .Replace("@", "")
            .Replace("#", "")
            .Replace("$", "")
            .Replace("%", "")
            .Replace("^", "")
            .Replace("*", "")
            .Replace("+", "")
            .Replace("=", "")
            .Replace("|", "")
            .Replace("\\", "")
            .Replace("/", "")
            .Replace("(", "")
            .Replace(")", "")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("{", "")
            .Replace("}", "")
            .Replace("<", "")
            .Replace(">", "");

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');

        return slug + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    private static DateTime? NormalizeToUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        if (value.Value.Kind == DateTimeKind.Utc)
            return value.Value;

        return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }
}
