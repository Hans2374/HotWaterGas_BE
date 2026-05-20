using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminCategoryService : IAdminCategoryService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ILogger<AdminCategoryService>? _logger;

    public AdminCategoryService(HotWaterGasDBContext dbContext, ILogger<AdminCategoryService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedResponse<CategoryListItemResponse>> GetCategoriesAsync(
        GetAdminCategoriesRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Categories
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchPattern = $"%{request.Search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.Name, searchPattern) ||
                EF.Functions.Like(c.Slug, searchPattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(c => c.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = query.OrderBy(c => c.Name).ThenBy(c => c.Id);

        var categories = await query
            .Skip(request.SkipCount)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var categoryIds = categories.Select(c => c.Id).ToList();
        var productCountDict = await GetProductCountsForCategoriesAsync(categoryIds, cancellationToken);

        var items = categories.Select(c => new CategoryListItemResponse
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            ImageUrl = c.ImageUrl,
            IsActive = c.IsActive,
            AttachedProductsCount = productCountDict.TryGetValue(c.Id, out var count) ? count : 0,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        _logger?.LogInformation(
            "[AdminCategory.List] PageNumber={PageNumber} PageSize={PageSize} SkipCount={SkipCount} Search={Search} IsActive={IsActive} ReturnedCount={ReturnedCount} TotalCount={TotalCount}",
            request.PageNumber, request.PageSize, request.SkipCount, request.Search, request.IsActive, items.Count, totalCount);

        return new PagedResponse<CategoryListItemResponse>
        {
            Items = items,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasPreviousPage = request.PageNumber > 1,
            HasNextPage = request.PageNumber < totalPages
        };
    }

    public async Task<CategoryDetailResponse?> GetCategoryByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            _logger?.LogWarning("[AdminCategory.GetById] Not found CategoryId={CategoryId}", id);
            return null;
        }

        var productCount = await CountProductLinksForCategoryAsync(id, cancellationToken);

        _logger?.LogInformation("[AdminCategory.GetById] CategoryId={CategoryId}", id);

        return new CategoryDetailResponse
        {
            Id = category.Id,
            Name = category.Name,
            Slug = category.Slug,
            ImageUrl = category.ImageUrl,
            IsActive = category.IsActive,
            AttachedProductsCount = productCount,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }

    public async Task<CategoryDetailResponse> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminCategory.ValidationFailed] Reason=EmptyName");
            throw new ApiException(400, "Category name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Name != null && c.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminCategory.ValidationFailed] Reason=DuplicateName Name={Name}", name);
            throw new ApiException(409, $"Category with name '{name}' already exists.");
        }

        var slug = !string.IsNullOrWhiteSpace(request.Slug)
            ? SlugGenerator.Generate(request.Slug)
            : SlugGenerator.Generate(name);

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Slug != null && c.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminCategory.ValidationFailed] Reason=DuplicateSlug Slug={Slug}", slug);
            throw new ApiException(409, $"Category with slug '{slug}' already exists.");
        }

        var category = new Categories
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            IsActive = request.IsActive,
            ImageUrl = request.ImageUrl
        };

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminCategory.Create] CategoryId={CategoryId} Name={Name} Slug={Slug}",
            category.Id, category.Name, category.Slug);

        return new CategoryDetailResponse
        {
            Id = category.Id,
            Name = category.Name,
            Slug = category.Slug,
            ImageUrl = category.ImageUrl,
            IsActive = category.IsActive,
            AttachedProductsCount = 0,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }

    public async Task<CategoryDetailResponse> UpdateCategoryAsync(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            _logger?.LogWarning("[AdminCategory.Update] Not found CategoryId={CategoryId}", id);
            throw new ApiException(404, $"Category with ID '{id}' not found.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminCategory.ValidationFailed] Reason=EmptyName CategoryId={CategoryId}", id);
            throw new ApiException(400, "Category name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Id != id && c.Name != null && c.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminCategory.ValidationFailed] Reason=DuplicateName Name={Name} CategoryId={CategoryId}", name, id);
            throw new ApiException(409, $"Category with name '{name}' already exists.");
        }

        var slug = SlugGenerator.Generate(request.Slug?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = SlugGenerator.Generate(name);
        }

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Id != id && c.Slug != null && c.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminCategory.ValidationFailed] Reason=DuplicateSlug Slug={Slug} CategoryId={CategoryId}", slug, id);
            throw new ApiException(409, $"Category with slug '{slug}' already exists.");
        }

        var previousName = category.Name;

        category.Name = name;
        category.Slug = slug;
        category.IsActive = request.IsActive;
        category.ImageUrl = request.ImageUrl;
        category.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var productCount = await CountProductLinksForCategoryAsync(id, cancellationToken);

        _logger?.LogInformation(
            "[AdminCategory.Update] CategoryId={CategoryId} PreviousName={PreviousName} NewName={NewName}",
            id, previousName, name);

        return new CategoryDetailResponse
        {
            Id = category.Id,
            Name = category.Name,
            Slug = category.Slug,
            ImageUrl = category.ImageUrl,
            IsActive = category.IsActive,
            AttachedProductsCount = productCount,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }

    public async Task DeleteCategoryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            _logger?.LogWarning("[AdminCategory.Delete] Not found CategoryId={CategoryId}", id);
            throw new ApiException(404, $"Category with ID '{id}' not found.");
        }

        var productCount = await CountProductLinksForCategoryAsync(id, cancellationToken);

        if (productCount > 0)
        {
            _logger?.LogWarning(
                "[AdminCategory.DeleteBlocked] CategoryId={CategoryId} Name={Name} ProductCount={ProductCount}",
                id, category.Name, productCount);
            throw new ApiException(400, "Cannot delete category because it is attached to existing products.");
        }

        var categoryName = category.Name;

        _dbContext.Categories.Remove(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminCategory.Delete] CategoryId={CategoryId} Name={Name}",
            id, categoryName);
    }

    private async Task<Dictionary<Guid, int>> GetProductCountsForCategoriesAsync(
        List<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0) return new Dictionary<Guid, int>();

        var results = await _dbContext.ProductCategories
            .AsNoTracking()
            .Where(pc => categoryIds.Contains(pc.CategoryId))
            .GroupBy(pc => pc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return results.ToDictionary(r => r.CategoryId, r => r.Count);
    }

    private async Task<int> CountProductLinksForCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        return await _dbContext.ProductCategories
            .AsNoTracking()
            .CountAsync(pc => pc.CategoryId == categoryId, cancellationToken);
    }
}
