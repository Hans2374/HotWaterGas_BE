using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminTagService : IAdminTagService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ILogger<AdminTagService>? _logger;

    public AdminTagService(HotWaterGasDBContext dbContext, ILogger<AdminTagService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedResponse<TagListItemResponse>> GetTagsAsync(
        GetAdminTagsRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Tags
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchPattern = $"%{request.Search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.Like(t.Name, searchPattern) ||
                EF.Functions.Like(t.Slug, searchPattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(t => t.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = query.OrderBy(t => t.Name).ThenBy(t => t.Id);

        var tags = await query
            .Skip(request.SkipCount)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var tagIds = tags.Select(t => t.Id).ToList();
        var productCountDict = await GetProductCountsForTagsAsync(tagIds, cancellationToken);

        var items = tags.Select(t => new TagListItemResponse
        {
            Id = t.Id,
            Name = t.Name,
            Slug = t.Slug,
            IsActive = t.IsActive,
            AttachedProductsCount = productCountDict.TryGetValue(t.Id, out var count) ? count : 0,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        _logger?.LogInformation(
            "[AdminTag.List] PageNumber={PageNumber} PageSize={PageSize} SkipCount={SkipCount} Search={Search} IsActive={IsActive} ReturnedCount={ReturnedCount} TotalCount={TotalCount}",
            request.PageNumber, request.PageSize, request.SkipCount, request.Search, request.IsActive, items.Count, totalCount);

        return new PagedResponse<TagListItemResponse>
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

    public async Task<TagDetailResponse?> GetTagByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tag = await _dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (tag is null)
        {
            _logger?.LogWarning("[AdminTag.GetById] Not found TagId={TagId}", id);
            return null;
        }

        var productCount = await CountProductLinksForTagAsync(id, cancellationToken);

        _logger?.LogInformation("[AdminTag.GetById] TagId={TagId}", id);

        return new TagDetailResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug,
            IsActive = tag.IsActive,
            AttachedProductsCount = productCount,
            CreatedAt = tag.CreatedAt,
            UpdatedAt = tag.UpdatedAt
        };
    }

    public async Task<TagDetailResponse> CreateTagAsync(
        CreateTagRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminTag.ValidationFailed] Reason=EmptyName");
            throw new ApiException(400, "Tag name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Tags
            .AsNoTracking()
            .AnyAsync(t => t.Name != null && t.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminTag.ValidationFailed] Reason=DuplicateName Name={Name}", name);
            throw new ApiException(409, $"Tag with name '{name}' already exists.");
        }

        var slug = !string.IsNullOrWhiteSpace(request.Slug)
            ? SlugGenerator.Generate(request.Slug)
            : SlugGenerator.Generate(name);

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Tags
            .AsNoTracking()
            .AnyAsync(t => t.Slug != null && t.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminTag.ValidationFailed] Reason=DuplicateSlug Slug={Slug}", slug);
            throw new ApiException(409, $"Tag with slug '{slug}' already exists.");
        }

        var tag = new Tags
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            IsActive = request.IsActive
        };

        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminTag.Create] TagId={TagId} Name={Name} Slug={Slug}",
            tag.Id, tag.Name, tag.Slug);

        return new TagDetailResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug,
            IsActive = tag.IsActive,
            AttachedProductsCount = 0,
            CreatedAt = tag.CreatedAt,
            UpdatedAt = tag.UpdatedAt
        };
    }

    public async Task<TagDetailResponse> UpdateTagAsync(
        Guid id,
        UpdateTagRequest request,
        CancellationToken cancellationToken = default)
    {
        var tag = await _dbContext.Tags
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (tag is null)
        {
            _logger?.LogWarning("[AdminTag.Update] Not found TagId={TagId}", id);
            throw new ApiException(404, $"Tag with ID '{id}' not found.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.LogWarning("[AdminTag.ValidationFailed] Reason=EmptyName TagId={TagId}", id);
            throw new ApiException(400, "Tag name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Tags
            .AsNoTracking()
            .AnyAsync(t => t.Id != id && t.Name != null && t.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger?.LogWarning("[AdminTag.ValidationFailed] Reason=DuplicateName Name={Name} TagId={TagId}", name, id);
            throw new ApiException(409, $"Tag with name '{name}' already exists.");
        }

        var slug = SlugGenerator.Generate(request.Slug?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = SlugGenerator.Generate(name);
        }

        var slugLower = slug.ToLowerInvariant();
        var existingBySlug = await _dbContext.Tags
            .AsNoTracking()
            .AnyAsync(t => t.Id != id && t.Slug != null && t.Slug.ToLower() == slugLower, cancellationToken);

        if (existingBySlug)
        {
            _logger?.LogWarning("[AdminTag.ValidationFailed] Reason=DuplicateSlug Slug={Slug} TagId={TagId}", slug, id);
            throw new ApiException(409, $"Tag with slug '{slug}' already exists.");
        }

        var previousName = tag.Name;

        tag.Name = name;
        tag.Slug = slug;
        tag.IsActive = request.IsActive;
        tag.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var productCount = await CountProductLinksForTagAsync(id, cancellationToken);

        _logger?.LogInformation(
            "[AdminTag.Update] TagId={TagId} PreviousName={PreviousName} NewName={NewName}",
            id, previousName, name);

        return new TagDetailResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug,
            IsActive = tag.IsActive,
            AttachedProductsCount = productCount,
            CreatedAt = tag.CreatedAt,
            UpdatedAt = tag.UpdatedAt
        };
    }

    public async Task DeleteTagAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tag = await _dbContext.Tags
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (tag is null)
        {
            _logger?.LogWarning("[AdminTag.Delete] Not found TagId={TagId}", id);
            throw new ApiException(404, $"Tag with ID '{id}' not found.");
        }

        var productCount = await CountProductLinksForTagAsync(id, cancellationToken);

        if (productCount > 0)
        {
            _logger?.LogWarning(
                "[AdminTag.DeleteBlocked] TagId={TagId} Name={Name} ProductCount={ProductCount}",
                id, tag.Name, productCount);
            throw new ApiException(400, "Cannot delete tag because it is attached to existing products.");
        }

        var tagName = tag.Name;

        _dbContext.Tags.Remove(tag);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "[AdminTag.Delete] TagId={TagId} Name={Name}",
            id, tagName);
    }

    private async Task<Dictionary<Guid, int>> GetProductCountsForTagsAsync(
        List<Guid> tagIds,
        CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0) return new Dictionary<Guid, int>();

        var idList = string.Join(",", tagIds.Select(id => $"'{id}'"));
        var sql = $"SELECT TagId AS TagId, COUNT(*) AS Count FROM ProductTags WHERE TagId IN ({idList}) GROUP BY TagId";

        var results = await _dbContext.Database
            .SqlQueryRaw<TagIdCount>(sql)
            .ToListAsync(cancellationToken);

        return results.ToDictionary(r => r.TagId, r => r.Count);
    }

    private async Task<int> CountProductLinksForTagAsync(Guid tagId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT COUNT(*) FROM ProductTags WHERE TagId = '{tagId}'";
        var result = await _dbContext.Database
            .SqlQueryRaw<int>(sql)
            .ToListAsync(cancellationToken);
        return result.FirstOrDefault();
    }

    private class TagIdCount
    {
        public Guid TagId { get; set; }
        public int Count { get; set; }
    }
}
