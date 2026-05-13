using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repos.Models;
using Services.DTOs;
using Services.Implementations;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminRoleService : IAdminRoleService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ILogger<AdminRoleService> _logger;

    public AdminRoleService(HotWaterGasDBContext dbContext, ILogger<AdminRoleService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedResponse<RoleListItemResponse>> GetRolesAsync(
        GetAdminRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Roles
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchPattern = $"%{request.Search.Trim()}%";
            query = query.Where(r =>
                EF.Functions.Like(r.Name, searchPattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = query.OrderBy(r => r.Name).ThenBy(r => r.Id);

        var roles = await query
            .Skip(request.SkipCount)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var roleIds = roles.Select(r => r.Id).ToList();
        var userCountDict = await GetUserCountsForRolesAsync(roleIds, cancellationToken);

        var items = roles.Select(r => new RoleListItemResponse
        {
            Id = r.Id,
            Name = r.Name,
            UsersCount = userCountDict.TryGetValue(r.Id, out var count) ? count : 0
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        _logger.LogInformation(
            "[AdminRole.List] PageNumber={PageNumber} PageSize={PageSize} SkipCount={SkipCount} Search={Search} ReturnedCount={ReturnedCount} TotalCount={TotalCount}",
            request.PageNumber, request.PageSize, request.SkipCount, request.Search, items.Count, totalCount);

        return new PagedResponse<RoleListItemResponse>
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

    public async Task<RoleDetailResponse?> GetRoleByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (role is null)
        {
            _logger.LogWarning("[AdminRole.GetById] Not found RoleId={RoleId}", id);
            return null;
        }

        var usersCount = await CountUsersForRoleAsync(id, cancellationToken);

        _logger.LogInformation("[AdminRole.GetById] RoleId={RoleId}", id);

        return new RoleDetailResponse
        {
            Id = role.Id,
            Name = role.Name,
            UsersCount = usersCount
        };
    }

    public async Task<RoleDetailResponse> CreateRoleAsync(
        CreateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("[AdminRole.ValidationFailed] Reason=EmptyName");
            throw new ApiException(400, "Role name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Name != null && r.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger.LogWarning("[AdminRole.ValidationFailed] Reason=DuplicateName Name={Name}", name);
            throw new ApiException(409, $"Role with name '{name}' already exists.");
        }

        var role = new Roles
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[AdminRole.Create] RoleId={RoleId} Name={Name}",
            role.Id, role.Name);

        return new RoleDetailResponse
        {
            Id = role.Id,
            Name = role.Name,
            UsersCount = 0
        };
    }

    public async Task<RoleDetailResponse> UpdateRoleAsync(
        Guid id,
        UpdateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (role is null)
        {
            _logger.LogWarning("[AdminRole.Update] Not found RoleId={RoleId}", id);
            throw new ApiException(404, $"Role with ID '{id}' not found.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("[AdminRole.ValidationFailed] Reason=EmptyName RoleId={RoleId}", id);
            throw new ApiException(400, "Role name is required.");
        }

        var nameLower = name.ToLowerInvariant();
        var existingByName = await _dbContext.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Id != id && r.Name != null && r.Name.ToLower() == nameLower, cancellationToken);

        if (existingByName)
        {
            _logger.LogWarning("[AdminRole.ValidationFailed] Reason=DuplicateName Name={Name} RoleId={RoleId}", name, id);
            throw new ApiException(409, $"Role with name '{name}' already exists.");
        }

        var previousName = role.Name;

        role.Name = name;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var usersCount = await CountUsersForRoleAsync(id, cancellationToken);

        _logger.LogInformation(
            "[AdminRole.Update] RoleId={RoleId} PreviousName={PreviousName} NewName={NewName}",
            id, previousName, name);

        return new RoleDetailResponse
        {
            Id = role.Id,
            Name = role.Name,
            UsersCount = usersCount
        };
    }

    public async Task DeleteRoleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (role is null)
        {
            _logger.LogWarning("[AdminRole.Delete] Not found RoleId={RoleId}", id);
            throw new ApiException(404, $"Role with ID '{id}' not found.");
        }

        var usersCount = await CountUsersForRoleAsync(id, cancellationToken);

        if (usersCount > 0)
        {
            _logger.LogWarning(
                "[AdminRole.DeleteBlocked] RoleId={RoleId} Name={Name} UsersCount={UsersCount}",
                id, role.Name, usersCount);
            throw new ApiException(400, "Cannot delete role because it is assigned to existing users.");
        }

        var roleName = role.Name;

        _dbContext.Roles.Remove(role);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[AdminRole.Delete] RoleId={RoleId} Name={Name}",
            id, roleName);
    }

    private async Task<Dictionary<Guid, int>> GetUserCountsForRolesAsync(
        List<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0) return new Dictionary<Guid, int>();

        var idList = string.Join(",", roleIds.Select(id => $"'{id}'"));
        var sql = $"SELECT \"RoleId\" AS RoleId, COUNT(*) AS Count FROM \"Users\" WHERE \"RoleId\" IN ({idList}) GROUP BY \"RoleId\"";

        var results = await _dbContext.Database
            .SqlQueryRaw<RoleIdCount>(sql)
            .ToListAsync(cancellationToken);

        return results.ToDictionary(r => r.RoleId, r => r.Count);
    }

    private async Task<int> CountUsersForRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT COUNT(*) FROM \"Users\" WHERE \"RoleId\" = '{roleId}'";
        var result = await _dbContext.Database
            .SqlQueryRaw<int>(sql)
            .ToListAsync(cancellationToken);
        return result.FirstOrDefault();
    }

    private class RoleIdCount
    {
        public Guid RoleId { get; set; }
        public int Count { get; set; }
    }
}
