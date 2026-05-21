using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Implementations;
using Services.Interfaces;

namespace Services.Implementations;

public class OrderService : IOrderService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public OrderService(HotWaterGasDBContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    private Guid RequireUserId()
    {
        return _currentUserService.UserId
            ?? throw new ApiException(401, "Yêu cầu xác thực.");
    }

    public async Task<List<MyOrderListItemResponse>> GetMyOrdersAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

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
        var userId = RequireUserId();

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

    public async Task<AdminOrderDetailResponse?> GetAdminOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.PaymentTransactions)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
                    .ThenInclude(p => p!.ProductImages)
            .Include(o => o.SteamKeys)
                .ThenInclude(sk => sk.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var items = order.OrderItems
            .Where(oi => oi.Product != null)
            .Select(oi => new AdminOrderItemResponse
            {
                ProductId = oi.ProductId,
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
            .Select(sk => new AdminOrderLicenseResponse
            {
                SteamKeyId = sk.Id,
                ProductName = sk.Product!.Name,
                KeyValue = sk.KeyValue,
                UsedAt = sk.UsedAt
            })
            .ToList();

        var paymentStatusLabel = order.PaymentTransactions != null
            ? MapPaymentStatusToLabel(order.PaymentTransactions.Status)
            : "No payment record";

        return new AdminOrderDetailResponse
        {
            OrderId = order.Id,
            OrderNumber = GenerateOrderNumber(order.CreatedAt, order.Id),
            CreatedAt = order.CreatedAt,
            FulfilledAt = order.FulfilledAt,
            Status = order.Status,
            StatusLabel = GetStatusLabel(order.Status),
            PaymentStatus = paymentStatusLabel,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            FinalTotal = order.FinalTotal,
            PaymentMethodLabel = order.PaymentTransactions != null
                ? GetPaymentMethodLabel(order.PaymentTransactions.Provider)
                : "Không xác định",
            Customer = new AdminOrderCustomerInfo
            {
                UserId = order.UserId,
                DisplayName = order.User?.DisplayName ?? string.Empty,
                Email = order.User?.Email ?? string.Empty
            },
            Items = items,
            Licenses = licenses
        };
    }

    private static string MapPaymentStatusToLabel(int status) => status switch
    {
        1 => "Pending",
        2 => "Paid",
        3 => "Cancelled",
        0 => "Failed",
        _ => "Unknown"
    };

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
}
