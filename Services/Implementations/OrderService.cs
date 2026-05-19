using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class OrderService : IOrderService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderService(HotWaterGasDBContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<List<MyOrderListItemResponse>> GetMyOrdersAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new MyOrderListItemResponse
            {
                OrderId = o.Id,
                OrderNumber = GenerateOrderNumber(o.CreatedAt, o.Id),
                CreatedAt = o.CreatedAt,
                Status = o.Status,
                StatusLabel = GetStatusLabel(o.Status),
                Total = o.FinalTotal,
                ItemCount = o.OrderItems.Sum(oi => oi.Quantity)
            })
            .ToListAsync(cancellationToken);

        return orders;
    }

    public async Task<MyOrderDetailResponse?> GetMyOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
                    .ThenInclude(p => p!.ProductImages)
            .Include(o => o.SteamKeys)
                .ThenInclude(sk => sk.Product)
            .Include(o => o.PaymentTransactions)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var items = order.OrderItems
            .Where(oi => oi.Product != null)
            .Select(oi => new MyOrderDetailItemResponse
            {
                ProductName = oi.Product!.Name,
                ProductSlug = oi.Product.Slug,
                ProductImageUrl = oi.Product.ProductImages
                    .OrderBy(i => i.IsPrimary ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault() ?? string.Empty,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                LineTotal = oi.LineTotal
            })
            .ToList();

        var licenses = order.SteamKeys
            .Where(sk => sk.Product != null)
            .Select(sk => new MyOrderLicenseResponse
            {
                ProductName = sk.Product!.Name,
                KeyValue = sk.KeyValue,
                RedemptionGuideUrl = "https://store.steampowered.com/account/registerkey"
            })
            .ToList();

        return new MyOrderDetailResponse
        {
            OrderId = order.Id,
            OrderNumber = GenerateOrderNumber(order.CreatedAt, order.Id),
            CreatedAt = order.CreatedAt,
            Status = order.Status,
            StatusLabel = GetStatusLabel(order.Status),
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            FinalTotal = order.FinalTotal,
            PaymentMethodLabel = order.PaymentTransactions != null
                ? GetPaymentMethodLabel(order.PaymentTransactions.Provider)
                : "Thanh toán QR",
            Items = items,
            Licenses = licenses
        };
    }

    private static string GenerateOrderNumber(DateTime createdAt, Guid orderId)
    {
        var shortId = orderId.ToString("N").Substring(0, 8).ToUpperInvariant();
        return $"HWG-{createdAt:yyyyMMdd}-{shortId}";
    }

    private static string GetStatusLabel(int status)
    {
        return status switch
        {
            0 => "Đã hủy",
            1 => "Thất bại",
            2 => "Đang chờ",
            4 => "Hoàn tất",
            _ => "Không xác định"
        };
    }

    private static string GetPaymentMethodLabel(int provider)
    {
        return provider switch
        {
            1 => "Thanh toán QR",
            _ => "Không xác định"
        };
    }

    private Guid GetCurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = user.FindFirst("UserId")?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }
        }
        return Guid.Empty;
    }
}
