using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly HotWaterGasDBContext _dbContext;

    public AdminDashboardService(HotWaterGasDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    // ─── Order Status Constants (matching existing OrderService conventions) ─────
    private const int OrderStatusCancelled = 0;
    private const int OrderStatusFailed = 1;
    private const int OrderStatusPending = 2;
    private const int OrderStatusCompleted = 4;

    // ─── Payment Transaction Status Constants ────────────────────────────────────
    private const int PaymentTxStatusPending = 1;
    private const int PaymentTxStatusPaid = 2;
    private const int PaymentTxStatusCancelled = 3;

    // ─── Steam Key Status Constants ──────────────────────────────────────────────
    private const int SteamKeyStatusAvailable = 0;

    // ─── Revenue Analytics ────────────────────────────────────────────────────────

    private static string MapOrderStatusToLabel(int status) => status switch
    {
        OrderStatusCancelled => "Cancelled",
        OrderStatusFailed => "Failed",
        OrderStatusPending => "Pending",
        OrderStatusCompleted => "Completed",
        3 => "Processing",
        _ => "Unknown"
    };

    private static string MapPaymentStatusToLabel(int status) => status switch
    {
        PaymentTxStatusPending => "Pending",
        PaymentTxStatusPaid => "Paid",
        PaymentTxStatusCancelled => "Cancelled",
        0 => "Failed",
        _ => "Unknown"
    };

    public async Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;

        // Revenue: sum FinalTotal for completed orders (Status == 4)
        var totalRevenue = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatusCompleted)
            .SumAsync(o => o.FinalTotal, cancellationToken);

        // Total orders (all non-deleted statuses)
        var totalOrders = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(cancellationToken);

        // Orders created today (UTC)
        var ordersToday = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(o => o.CreatedAt >= todayStart, cancellationToken);

        // Order status counts
        var completedOrders = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(o => o.Status == OrderStatusCompleted, cancellationToken);

        var cancelledOrders = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(o => o.Status == OrderStatusCancelled, cancellationToken);

        // Low stock: products with <= 5 available (active, unsold) Steam keys
        var lowStockProducts = await _dbContext.SteamKeys
            .AsNoTracking()
            .Where(sk => sk.Status == SteamKeyStatusAvailable)
            .GroupBy(sk => sk.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .Where(x => x.Count <= 5)
            .CountAsync(cancellationToken);

        // Total customers: count users whose role is NOT Admin
        var totalCustomers = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Role != null && u.Role.Name != "Admin")
            .CountAsync(cancellationToken);

        return new DashboardSummaryResponse
        {
            TotalRevenue = totalRevenue,
            TotalOrders = totalOrders,
            OrdersToday = ordersToday,
            CompletedOrders = completedOrders,
            CancelledOrders = cancelledOrders,
            LowStockProducts = lowStockProducts,
            TotalCustomers = totalCustomers
        };
    }

    public async Task<List<RevenueDataPoint>> GetRevenueAsync(string range, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return range.ToLowerInvariant() switch
        {
            "30d" => await GetRevenueDailyAsync(now.AddDays(-29).Date, now.Date, cancellationToken),
            "12m" => await GetRevenueMonthlyAsync(now.AddMonths(-11).Date, now.Date, cancellationToken),
            _ => await GetRevenueDailyAsync(now.AddDays(-6).Date, now.Date, cancellationToken)
        };
    }

    private async Task<List<RevenueDataPoint>> GetRevenueDailyAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        // Fetch daily revenue from completed orders
        var dailyRevenue = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatusCompleted && o.CreatedAt.Date >= startDate && o.CreatedAt.Date <= endDate)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.FinalTotal) })
            .ToListAsync(cancellationToken);

        var revenueByDate = dailyRevenue.ToDictionary(x => x.Date, x => x.Revenue);

        var result = new List<RevenueDataPoint>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            result.Add(new RevenueDataPoint
            {
                Label = date.ToString("yyyy-MM-dd"),
                Revenue = revenueByDate.TryGetValue(date, out var rev) ? rev : 0m
            });
        }

        return result;
    }

    private async Task<List<RevenueDataPoint>> GetRevenueMonthlyAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        // Fetch monthly revenue from completed orders
        var monthlyRevenue = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatusCompleted && o.CreatedAt.Date >= startDate && o.CreatedAt.Date <= endDate)
            .Select(o => new { Year = o.CreatedAt.Year, Month = o.CreatedAt.Month, Revenue = o.FinalTotal })
            .GroupBy(x => new { x.Year, x.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(x => x.Revenue) })
            .ToListAsync(cancellationToken);

        var revenueByMonth = monthlyRevenue
            .ToDictionary(
                x => (Year: x.Year, Month: x.Month),
                x => x.Revenue);

        var result = new List<RevenueDataPoint>();
        var current = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        while (current <= end)
        {
            var key = (Year: current.Year, Month: current.Month);
            result.Add(new RevenueDataPoint
            {
                Label = current.ToString("yyyy-MM"),
                Revenue = revenueByMonth.TryGetValue(key, out var rev) ? rev : 0m
            });
            current = current.AddMonths(1);
        }

        return result;
    }

    public async Task<List<OrderStatusCount>> GetOrderStatusAsync(CancellationToken cancellationToken = default)
    {
        var statusCounts = await _dbContext.Orders
            .AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return statusCounts
            .Select(x => new OrderStatusCount
            {
                Status = MapOrderStatusToLabel(x.Status),
                Count = x.Count
            })
            .OrderBy(x => x.Status)
            .ToList();
    }

    public async Task<List<LowStockProductItem>> GetLowStockProductsAsync(
        int threshold,
        CancellationToken cancellationToken = default)
    {
        var effectiveThreshold = threshold <= 0 ? 5 : threshold;

        // Get products with available (Status=0), unsold, unassigned Steam keys
        // matching the inventory business rules used throughout the codebase
        var lowStockProducts = await _dbContext.SteamKeys
            .AsNoTracking()
            .Where(sk => sk.Status == SteamKeyStatusAvailable)
            .GroupBy(sk => new { sk.ProductId, sk.Product!.Name, sk.Product.Slug, sk.Product.IsDeleted })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Name,
                g.Key.Slug,
                IsDeleted = g.Key.IsDeleted,
                AvailableKeys = g.Count()
            })
            .Where(x => x.AvailableKeys <= effectiveThreshold && !x.IsDeleted)
            .OrderBy(x => x.AvailableKeys)
            .ToListAsync(cancellationToken);

        // Fetch primary images in a separate efficient query
        var productIds = lowStockProducts.Select(p => p.ProductId).ToList();
        var primaryImages = await _dbContext.ProductImages
            .AsNoTracking()
            .Where(pi => productIds.Contains(pi.ProductId))
            .GroupBy(pi => pi.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                ImageUrl = g.OrderBy(i => i.IsPrimary ? 0 : 1)
                            .ThenBy(i => i.DisplayOrder)
                            .Select(i => i.ImageUrl)
                            .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var imageByProduct = primaryImages.ToDictionary(x => x.ProductId, x => x.ImageUrl ?? string.Empty);

        return lowStockProducts
            .Select(p => new LowStockProductItem
            {
                ProductId = p.ProductId,
                ProductSlug = p.Slug,
                ProductName = p.Name,
                ThumbnailImageUrl = imageByProduct.TryGetValue(p.ProductId, out var img) ? img : string.Empty,
                AvailableKeys = p.AvailableKeys,
                IsActive = !p.IsDeleted
            })
            .ToList();
    }

    public async Task<InventoryAlertsResponse> GetInventoryAlertsAsync(
        int threshold,
        CancellationToken cancellationToken = default)
    {
        var effectiveThreshold = threshold <= 0 ? 5 : threshold;

        // Fetch ALL non-deleted products with their available key counts
        // in a single efficient query using the canonical SteamKeys source
        var allProductStock = await _dbContext.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                AvailableKeys = p.SteamKeys.Count(k => k.Status == 0 && k.OrderId == null && k.InvalidatedAt == null)
            })
            .ToListAsync(cancellationToken);

        var outOfStock = allProductStock
            .Where(p => p.AvailableKeys == 0)
            .OrderBy(p => p.Name)
            .Select(p => new LowStockProductItem
            {
                ProductId = p.Id,
                ProductName = p.Name,
                ProductSlug = p.Slug,
                AvailableKeys = p.AvailableKeys,
                ThumbnailImageUrl = string.Empty,
                IsActive = true
            })
            .ToList();

        var lowStock = allProductStock
            .Where(p => p.AvailableKeys > 0 && p.AvailableKeys <= effectiveThreshold)
            .OrderBy(p => p.AvailableKeys)
            .Select(p => new LowStockProductItem
            {
                ProductId = p.Id,
                ProductName = p.Name,
                ProductSlug = p.Slug,
                AvailableKeys = p.AvailableKeys,
                ThumbnailImageUrl = string.Empty,
                IsActive = true
            })
            .ToList();

        // Batch-fetch thumbnails for both lists
        var allProductIds = outOfStock.Concat(lowStock)
            .Select(p => p.ProductId)
            .ToList();

        var imagesByProduct = new Dictionary<Guid, string>();
        if (allProductIds.Count > 0)
        {
            var images = await _dbContext.ProductImages
                .AsNoTracking()
                .Where(pi => allProductIds.Contains(pi.ProductId))
                .GroupBy(pi => pi.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    ImageUrl = g.OrderBy(i => i.IsPrimary ? 0 : 1)
                                .ThenBy(i => i.DisplayOrder)
                                .Select(i => i.ImageUrl)
                                .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            imagesByProduct = images.ToDictionary(x => x.ProductId, x => x.ImageUrl ?? string.Empty);
        }

        foreach (var product in outOfStock.Concat(lowStock))
        {
            product.ThumbnailImageUrl = imagesByProduct.TryGetValue(product.ProductId, out var url) ? url : string.Empty;
        }

        return new InventoryAlertsResponse
        {
            OutOfStockProducts = outOfStock,
            LowStockProducts = lowStock
        };
    }

    public async Task<PagedResponse<RecentOrderItem>> GetRecentOrdersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 50);

        var totalCount = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.PaymentTransactions)
            .Include(o => o.OrderItems)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(o => new
            {
                o.Id,
                o.CreatedAt,
                o.FinalTotal,
                o.Status,
                PaymentStatus = o.PaymentTransactions != null ? o.PaymentTransactions.Status : (int?)null,
                o.User,
                ItemCount = o.OrderItems.Sum(oi => oi.Quantity)
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var items = orders.Select(o => new RecentOrderItem
        {
            OrderId = o.Id,
            OrderCode = $"HWG-{o.CreatedAt:yyyyMMdd}-{o.Id.ToString("N")[..8].ToUpperInvariant()}",
            CustomerName = o.User?.DisplayName ?? string.Empty,
            CustomerEmail = o.User?.Email ?? string.Empty,
            TotalAmount = o.FinalTotal,
            ItemCount = o.ItemCount,
            OrderStatus = MapOrderStatusToLabel(o.Status),
            PaymentStatus = o.PaymentStatus.HasValue ? MapPaymentStatusToLabel(o.PaymentStatus.Value) : "Pending",
            CreatedAt = o.CreatedAt
        }).ToList();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)safePageSize);

        return new PagedResponse<RecentOrderItem>
        {
            Items = items,
            PageNumber = safePage,
            PageSize = safePageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasPreviousPage = safePage > 1,
            HasNextPage = safePage < totalPages
        };
    }
}
