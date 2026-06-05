using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminPublisherService : IAdminPublisherService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ILogger<AdminPublisherService>? _logger;

    public AdminPublisherService(HotWaterGasDBContext dbContext, ILogger<AdminPublisherService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedResponse<PublisherListItemResponse>> GetPublishersAsync(
        GetAdminPublishersRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Publishers
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchPattern = $"%{request.Search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, searchPattern) ||
                EF.Functions.Like(p.Slug, searchPattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = query.OrderBy(p => p.Name).ThenBy(p => p.Id);

        var publishers = await query
            .Skip(request.SkipCount)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var publisherIds = publishers.Select(p => p.Id).ToList();
        var productCountDict = await GetProductCountsForPublishersAsync(publisherIds, cancellationToken);

        var items = publishers.Select(p => new PublisherListItemResponse
        {
            Id = p.Id,
            Name = p.Name,
            Slug = p.Slug,
            LogoUrl = p.LogoUrl,
            Description = p.Description,
            AttachedProductsCount = productCountDict.TryGetValue(p.Id, out var count) ? count : 0,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        _logger?.LogInformation(
            "[AdminPublisher.List] PageNumber={PageNumber} PageSize={PageSize} SkipCount={SkipCount} Search={Search} ReturnedCount={ReturnedCount} TotalCount={TotalCount}",
            request.PageNumber, request.PageSize, request.SkipCount, request.Search, items.Count, totalCount);

        return new PagedResponse<PublisherListItemResponse>
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

    public async Task<PublisherDetailResponse?> GetPublisherByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var publisher = await _dbContext.Publishers
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (publisher is null)
        {
            _logger?.LogWarning("[AdminPublisher.GetById] Not found PublisherId={PublisherId}", id);
            return null;
        }

        var productCount = await CountProductsForPublisherAsync(id, cancellationToken);

        _logger?.LogInformation("[AdminPublisher.GetById] PublisherId={PublisherId}", id);

        return new PublisherDetailResponse
        {
            Id = publisher.Id,
            Name = publisher.Name,
            Slug = publisher.Slug,
            LogoUrl = publisher.LogoUrl,
            Description = publisher.Description,
            AttachedProductsCount = productCount,
            CreatedAt = publisher.CreatedAt,
            UpdatedAt = publisher.UpdatedAt
        };
    }

    public async Task<PublisherDetailResponse> CreatePublisherAsync(
        CreatePublisherRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminPublisher.ValidationFailed] Reason=EmptyName");
            throw new ApiException(400, "Publisher name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Publishers
            .AsNoTracking()
            .AnyAsync(p => p.Name != null && p.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminPublisher.ValidationFailed] Reason=DuplicateName Name={Name}", name);
            throw new ApiException(409, $"Publisher with name '{name}' already exists.");
        }

        var slug = !string.IsNullOrWhiteSpace(request.Slug)
            ? SlugGenerator.Generate(request.Slug)
            : SlugGenerator.Generate(name);

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Publishers
            .AsNoTracking()
            .AnyAsync(p => p.Slug != null && p.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminPublisher.ValidationFailed] Reason=DuplicateSlug Slug={Slug}", slug);
            throw new ApiException(409, $"Publisher with slug '{slug}' already exists.");
        }

        var publisher = new Publishers
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            LogoUrl = request.LogoUrl,
            Description = request.Description
        };

        _dbContext.Publishers.Add(publisher);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminPublisher.Create] PublisherId={PublisherId} Name={Name} Slug={Slug}",
            publisher.Id, publisher.Name, publisher.Slug);

        return new PublisherDetailResponse
        {
            Id = publisher.Id,
            Name = publisher.Name,
            Slug = publisher.Slug,
            LogoUrl = publisher.LogoUrl,
            Description = publisher.Description,
            AttachedProductsCount = 0,
            CreatedAt = publisher.CreatedAt,
            UpdatedAt = publisher.UpdatedAt
        };
    }

    public async Task<PublisherDetailResponse> UpdatePublisherAsync(
        Guid id,
        UpdatePublisherRequest request,
        CancellationToken cancellationToken = default)
    {
        var publisher = await _dbContext.Publishers
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (publisher is null)
        {
            _logger?.LogWarning("[AdminPublisher.Update] Not found PublisherId={PublisherId}", id);
            throw new ApiException(404, $"Publisher with ID '{id}' not found.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminPublisher.ValidationFailed] Reason=EmptyName PublisherId={PublisherId}", id);
            throw new ApiException(400, "Publisher name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Publishers
            .AsNoTracking()
            .AnyAsync(p => p.Id != id && p.Name != null && p.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminPublisher.ValidationFailed] Reason=DuplicateName Name={Name} PublisherId={PublisherId}", name, id);
            throw new ApiException(409, $"Publisher with name '{name}' already exists.");
        }

        var slug = SlugGenerator.Generate(request.Slug?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = SlugGenerator.Generate(name);
        }

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Publishers
            .AsNoTracking()
            .AnyAsync(p => p.Id != id && p.Slug != null && p.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminPublisher.ValidationFailed] Reason=DuplicateSlug Slug={Slug} PublisherId={PublisherId}", slug, id);
            throw new ApiException(409, $"Publisher with slug '{slug}' already exists.");
        }

        var previousName = publisher.Name;

        publisher.Name = name;
        publisher.Slug = slug;
        publisher.LogoUrl = request.LogoUrl;
        publisher.Description = request.Description;
        publisher.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var productCount = await CountProductsForPublisherAsync(id, cancellationToken);

        _logger?.LogInformation(
            "[AdminPublisher.Update] PublisherId={PublisherId} PreviousName={PreviousName} NewName={NewName}",
            id, previousName, name);

        return new PublisherDetailResponse
        {
            Id = publisher.Id,
            Name = publisher.Name,
            Slug = publisher.Slug,
            LogoUrl = publisher.LogoUrl,
            Description = publisher.Description,
            AttachedProductsCount = productCount,
            CreatedAt = publisher.CreatedAt,
            UpdatedAt = publisher.UpdatedAt
        };
    }

    public async Task DeletePublisherAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var publisher = await _dbContext.Publishers
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (publisher is null)
        {
            _logger?.LogWarning("[AdminPublisher.Delete] Not found PublisherId={PublisherId}", id);
            throw new ApiException(404, $"Publisher with ID '{id}' not found.");
        }

        var productCount = await CountProductsForPublisherAsync(id, cancellationToken);

        if (productCount > 0)
        {
            _logger?.LogWarning(
                "[AdminPublisher.DeleteBlocked] PublisherId={PublisherId} Name={Name} ProductCount={ProductCount}",
                id, publisher.Name, productCount);
            throw new ApiException(400, "Cannot delete publisher because it is attached to existing products.");
        }

        var publisherName = publisher.Name;

        _dbContext.Publishers.Remove(publisher);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminPublisher.Delete] PublisherId={PublisherId} Name={Name}",
            id, publisherName);
    }

    private async Task<Dictionary<Guid, int>> GetProductCountsForPublishersAsync(
        List<Guid> publisherIds,
        CancellationToken cancellationToken)
    {
        if (publisherIds.Count == 0) return new Dictionary<Guid, int>();

        var results = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.PublisherId.HasValue && publisherIds.Contains(p.PublisherId.Value))
            .GroupBy(p => p.PublisherId!.Value)
            .Select(g => new { PublisherId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return results.ToDictionary(r => r.PublisherId, r => r.Count);
    }

    private async Task<int> CountProductsForPublisherAsync(Guid publisherId, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .CountAsync(p => p.PublisherId == publisherId, cancellationToken);
    }
}
