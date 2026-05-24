using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminUserService : IAdminUserService
{
    private readonly HotWaterGasDBContext _dbContext;

    public AdminUserService(HotWaterGasDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedAdminUserListResponse> GetUsersAsync(AdminUserQueryRequest query, CancellationToken cancellationToken = default)
    {
        var safePage = query.Page < 1 ? 1 : query.Page;
        var safePageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 100);

        // Efficient projection-only query
        var baseQuery = _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.RefreshTokens)
            .AsQueryable();

        // Search filter: email or display name
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchTerm = query.Search.Trim().ToLower();
            baseQuery = baseQuery.Where(u =>
                EF.Functions.ILike(u.Email, $"%{searchTerm}%") ||
                EF.Functions.ILike(u.DisplayName, $"%{searchTerm}%"));
        }

        // Role filter
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            baseQuery = baseQuery.Where(u => u.Role.Name == query.Role);
        }

        // Status filter
        if (query.IsSuspended.HasValue)
        {
            baseQuery = baseQuery.Where(u => u.IsSuspended == query.IsSuspended.Value);
        }

        // Get total count before pagination
        var totalItems = await baseQuery.CountAsync(cancellationToken);

        // Fetch paginated users with aggregation
        var users = await baseQuery
            .OrderByDescending(u => u.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(u => new
            {
                User = u,
                RoleName = u.Role.Name,
                LastLoginAt = u.RefreshTokens
                    .OrderByDescending(rt => rt.CreatedAtUtc)
                    .Select(rt => (DateTime?)rt.CreatedAtUtc)
                    .FirstOrDefault(),
                OrdersCount = u.Orders.Count(o => o.Status != 3),
                TotalSpent = u.Orders
                    .Where(o => o.Status != 3)
                    .Sum(o => o.FinalTotal)
            })
            .ToListAsync(cancellationToken);

        // Project to DTOs
        var data = users.Select(x => new AdminUserTableItemDto
        {
            Id = x.User.Id,
            DisplayName = x.User.DisplayName,
            Email = x.User.Email,
            Role = x.RoleName,
            Provider = !string.IsNullOrEmpty(x.User.GoogleId) ? "Google" : "Local",
            IsSuspended = x.User.IsSuspended,
            OrdersCount = x.OrdersCount,
            TotalSpent = x.TotalSpent,
            LastLoginAt = x.LastLoginAt,
            CreatedAt = x.User.CreatedAt
        }).ToList();

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)safePageSize);

        return new PagedAdminUserListResponse
        {
            Data = data,
            Page = safePage,
            PageSize = safePageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public async Task ToggleSuspensionAsync(Guid userId, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        // Prevent admin from suspending themselves
        if (userId == adminUserId)
        {
            throw new InvalidOperationException("You cannot suspend your own account.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        user.IsSuspended = !user.IsSuspended;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminUserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        // Get last login from refresh tokens
        var lastLoginAt = _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .OrderByDescending(rt => rt.CreatedAtUtc)
            .Select(rt => (DateTime?)rt.CreatedAtUtc)
            .FirstOrDefault();

        // Get order stats (exclude cancelled orders - Status 3)
        var ordersQuery = _dbContext.Orders
            .Where(o => o.UserId == userId && o.Status != 3);

        var ordersCount = await ordersQuery.CountAsync(cancellationToken);
        var totalSpent = await ordersQuery.SumAsync(o => o.FinalTotal, cancellationToken);

        // Get recent 5 orders
        var recentOrders = await ordersQuery
            .OrderByDescending(o => o.CreatedAt)
            .Take(5)
            .Select(o => new AdminUserRecentOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                TotalAmount = o.FinalTotal,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync(cancellationToken);

        // Map order status to readable string
        var statusMap = new Dictionary<string, string>
        {
            { "0", "Pending" },
            { "1", "Confirmed" },
            { "2", "Completed" },
            { "3", "Cancelled" },
            { "4", "Refunded" }
        };

        foreach (var order in recentOrders)
        {
            order.Status = statusMap.GetValueOrDefault(order.Status, order.Status);
        }

        return new AdminUserDetailDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role?.Name ?? "Customer",
            Provider = !string.IsNullOrEmpty(user.GoogleId) ? "Google" : "Local",
            IsSuspended = user.IsSuspended,
            EmailConfirmed = user.IsEmailVerified,
            CreatedAt = user.CreatedAt,
            LastLoginAt = lastLoginAt,
            OrdersCount = ordersCount,
            TotalSpent = totalSpent,
            RecentOrders = recentOrders
        };
    }
}
