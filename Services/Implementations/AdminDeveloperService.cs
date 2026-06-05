using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminDeveloperService : IAdminDeveloperService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ILogger<AdminDeveloperService>? _logger;

    public AdminDeveloperService(HotWaterGasDBContext dbContext, ILogger<AdminDeveloperService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedResponse<DeveloperListItemResponse>> GetDevelopersAsync(
        GetAdminDevelopersRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Developers
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchPattern = $"%{request.Search.Trim()}%";
            query = query.Where(d =>
                EF.Functions.Like(d.Name, searchPattern) ||
                EF.Functions.Like(d.Slug, searchPattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = query.OrderBy(d => d.Name).ThenBy(d => d.Id);

        var developers = await query
            .Skip(request.SkipCount)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var developerIds = developers.Select(d => d.Id).ToList();
        var productCountDict = await GetProductCountsForDevelopersAsync(developerIds, cancellationToken);

        var items = developers.Select(d => new DeveloperListItemResponse
        {
            Id = d.Id,
            Name = d.Name,
            Slug = d.Slug,
            LogoUrl = d.LogoUrl,
            Description = d.Description,
            AttachedProductsCount = productCountDict.TryGetValue(d.Id, out var count) ? count : 0,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        _logger?.LogInformation(
            "[AdminDeveloper.List] PageNumber={PageNumber} PageSize={PageSize} SkipCount={SkipCount} Search={Search} ReturnedCount={ReturnedCount} TotalCount={TotalCount}",
            request.PageNumber, request.PageSize, request.SkipCount, request.Search, items.Count, totalCount);

        return new PagedResponse<DeveloperListItemResponse>
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

    public async Task<DeveloperDetailResponse?> GetDeveloperByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var developer = await _dbContext.Developers
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (developer is null)
        {
            _logger?.LogWarning("[AdminDeveloper.GetById] Not found DeveloperId={DeveloperId}", id);
            return null;
        }

        var productCount = await CountProductsForDeveloperAsync(id, cancellationToken);

        _logger?.LogInformation("[AdminDeveloper.GetById] DeveloperId={DeveloperId}", id);

        return new DeveloperDetailResponse
        {
            Id = developer.Id,
            Name = developer.Name,
            Slug = developer.Slug,
            LogoUrl = developer.LogoUrl,
            Description = developer.Description,
            AttachedProductsCount = productCount,
            CreatedAt = developer.CreatedAt,
            UpdatedAt = developer.UpdatedAt
        };
    }

    public async Task<DeveloperDetailResponse> CreateDeveloperAsync(
        CreateDeveloperRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminDeveloper.ValidationFailed] Reason=EmptyName");
            throw new ApiException(400, "Developer name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Developers
            .AsNoTracking()
            .AnyAsync(d => d.Name != null && d.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminDeveloper.ValidationFailed] Reason=DuplicateName Name={Name}", name);
            throw new ApiException(409, $"Developer with name '{name}' already exists.");
        }

        var slug = !string.IsNullOrWhiteSpace(request.Slug)
            ? SlugGenerator.Generate(request.Slug)
            : SlugGenerator.Generate(name);

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Developers
            .AsNoTracking()
            .AnyAsync(d => d.Slug != null && d.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminDeveloper.ValidationFailed] Reason=DuplicateSlug Slug={Slug}", slug);
            throw new ApiException(409, $"Developer with slug '{slug}' already exists.");
        }

        var developer = new Developers
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            LogoUrl = request.LogoUrl,
            Description = request.Description
        };

        _dbContext.Developers.Add(developer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminDeveloper.Create] DeveloperId={DeveloperId} Name={Name} Slug={Slug}",
            developer.Id, developer.Name, developer.Slug);

        return new DeveloperDetailResponse
        {
            Id = developer.Id,
            Name = developer.Name,
            Slug = developer.Slug,
            LogoUrl = developer.LogoUrl,
            Description = developer.Description,
            AttachedProductsCount = 0,
            CreatedAt = developer.CreatedAt,
            UpdatedAt = developer.UpdatedAt
        };
    }

    public async Task<DeveloperDetailResponse> UpdateDeveloperAsync(
        Guid id,
        UpdateDeveloperRequest request,
        CancellationToken cancellationToken = default)
    {
        var developer = await _dbContext.Developers
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (developer is null)
        {
            _logger?.LogWarning("[AdminDeveloper.Update] Not found DeveloperId={DeveloperId}", id);
            throw new ApiException(404, $"Developer with ID '{id}' not found.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminDeveloper.ValidationFailed] Reason=EmptyName DeveloperId={DeveloperId}", id);
            throw new ApiException(400, "Developer name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Developers
            .AsNoTracking()
            .AnyAsync(d => d.Id != id && d.Name != null && d.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminDeveloper.ValidationFailed] Reason=DuplicateName Name={Name} DeveloperId={DeveloperId}", name, id);
            throw new ApiException(409, $"Developer with name '{name}' already exists.");
        }

        var slug = SlugGenerator.Generate(request.Slug?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = SlugGenerator.Generate(name);
        }

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Developers
            .AsNoTracking()
            .AnyAsync(d => d.Id != id && d.Slug != null && d.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminDeveloper.ValidationFailed] Reason=DuplicateSlug Slug={Slug} DeveloperId={DeveloperId}", slug, id);
            throw new ApiException(409, $"Developer with slug '{slug}' already exists.");
        }

        var previousName = developer.Name;

        developer.Name = name;
        developer.Slug = slug;
        developer.LogoUrl = request.LogoUrl;
        developer.Description = request.Description;
        developer.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var productCount = await CountProductsForDeveloperAsync(id, cancellationToken);

        _logger?.LogInformation(
            "[AdminDeveloper.Update] DeveloperId={DeveloperId} PreviousName={PreviousName} NewName={NewName}",
            id, previousName, name);

        return new DeveloperDetailResponse
        {
            Id = developer.Id,
            Name = developer.Name,
            Slug = developer.Slug,
            LogoUrl = developer.LogoUrl,
            Description = developer.Description,
            AttachedProductsCount = productCount,
            CreatedAt = developer.CreatedAt,
            UpdatedAt = developer.UpdatedAt
        };
    }

    public async Task DeleteDeveloperAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var developer = await _dbContext.Developers
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (developer is null)
        {
            _logger?.LogWarning("[AdminDeveloper.Delete] Not found DeveloperId={DeveloperId}", id);
            throw new ApiException(404, $"Developer with ID '{id}' not found.");
        }

        var productCount = await CountProductsForDeveloperAsync(id, cancellationToken);

        if (productCount > 0)
        {
            _logger?.LogWarning(
                "[AdminDeveloper.DeleteBlocked] DeveloperId={DeveloperId} Name={Name} ProductCount={ProductCount}",
                id, developer.Name, productCount);
            throw new ApiException(400, "Cannot delete developer because it is attached to existing products.");
        }

        var developerName = developer.Name;

        _dbContext.Developers.Remove(developer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminDeveloper.Delete] DeveloperId={DeveloperId} Name={Name}",
            id, developerName);
    }

    private async Task<Dictionary<Guid, int>> GetProductCountsForDevelopersAsync(
        List<Guid> developerIds,
        CancellationToken cancellationToken)
    {
        if (developerIds.Count == 0) return new Dictionary<Guid, int>();

        var results = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.DeveloperId.HasValue && developerIds.Contains(p.DeveloperId.Value))
            .GroupBy(p => p.DeveloperId!.Value)
            .Select(g => new { DeveloperId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return results.ToDictionary(r => r.DeveloperId, r => r.Count);
    }

    private async Task<int> CountProductsForDeveloperAsync(Guid developerId, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .CountAsync(p => p.DeveloperId == developerId, cancellationToken);
    }
}
