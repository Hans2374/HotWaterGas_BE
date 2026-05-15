using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayOS;
using PayOS.Models;
using PayOS.Models.V2.PaymentRequests;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class CheckoutService : ICheckoutService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutService>? _logger;

    public CheckoutService(
        HotWaterGasDBContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<CheckoutService>? logger = null)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<CheckoutPreviewResponse> PreviewCheckoutAsync(
        List<Guid> cartItemIds,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        if (cartItemIds == null || cartItemIds.Count == 0)
        {
            return EmptyPreview();
        }

        var cartItemIdSet = cartItemIds.ToHashSet();

        var cartItems = await _dbContext.CartItems
            .AsNoTracking()
            .Include(ci => ci.Cart)
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.Discount)
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.ProductImages)
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.SteamKeys)
            .Where(ci => ci.Cart!.UserId == userId && cartItemIdSet.Contains(ci.Id))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var validItems = new List<CheckoutPreviewItemResponse>();
        var invalidItems = new List<CheckoutInvalidItemResponse>();
        var blockingMessages = new List<string>();

        foreach (var ci in cartItems)
        {
            if (ci.Product == null || ci.Product.IsDeleted)
            {
                invalidItems.Add(new CheckoutInvalidItemResponse
                {
                    CartItemId = ci.Id,
                    ProductId = ci.ProductId,
                    ProductName = "Unknown product",
                    ReasonCode = "PRODUCT_NOT_FOUND",
                    Message = "This product is no longer available."
                });
                continue;
            }

            var isActiveDiscount = ci.Product.Discount != null
                && ci.Product.Discount.StartDate <= now
                && ci.Product.Discount.EndDate >= now;

            var hasDiscount = isActiveDiscount;
            var discountPercentage = hasDiscount ? ci.Product.Discount!.Percentage : 0m;
            var discountPrice = hasDiscount
                ? Math.Round(ci.Product.Price * (1 - discountPercentage / 100m), 0, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            var unitPrice = hasDiscount && discountPrice.HasValue ? discountPrice.Value : ci.Product.Price;
            var lineTotal = Math.Round(unitPrice * ci.Quantity, 0, MidpointRounding.AwayFromZero);

            var imageUrl = ci.Product.ProductImages
                .OrderBy(i => i.IsPrimary ? 0 : 1)
                .ThenBy(i => i.DisplayOrder)
                .Select(i => i.ImageUrl)
                .FirstOrDefault() ?? string.Empty;

            // Compute actual available stock from Steam keys (canonical source)
            var computedStock = ci.Product.SteamKeys
                .Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null);

            if (computedStock < ci.Quantity)
            {
                invalidItems.Add(new CheckoutInvalidItemResponse
                {
                    CartItemId = ci.Id,
                    ProductId = ci.ProductId,
                    ProductName = ci.Product.Name,
                    ReasonCode = "INSUFFICIENT_STOCK",
                    Message = $"Only {computedStock} unit(s) in stock. Requested: {ci.Quantity}."
                });
                continue;
            }

            validItems.Add(new CheckoutPreviewItemResponse
            {
                CartItemId = ci.Id,
                ProductId = ci.ProductId,
                ProductName = ci.Product.Name,
                ProductSlug = ci.Product.Slug,
                ProductImage = imageUrl,
                Price = ci.Product.Price,
                DiscountPercentage = discountPercentage,
                DiscountPrice = discountPrice,
                HasDiscount = hasDiscount,
                UnitPrice = unitPrice,
                Quantity = ci.Quantity,
                LineTotal = lineTotal
            });
        }

        var missingIds = cartItemIdSet.Except(cartItems.Select(ci => ci.Id).ToHashSet());
        foreach (var missingId in missingIds)
        {
            invalidItems.Add(new CheckoutInvalidItemResponse
            {
                CartItemId = missingId,
                ProductId = Guid.Empty,
                ProductName = "Unknown item",
                ReasonCode = "CART_ITEM_NOT_FOUND",
                Message = "This cart item could not be found in your cart."
            });
        }

        if (validItems.Count == 0 && invalidItems.Count > 0)
        {
            blockingMessages.Add("No valid items to checkout. Please remove unavailable items.");
        }

        var subtotal = validItems.Sum(i => i.LineTotal);
        var finalTotal = subtotal;

        return new CheckoutPreviewResponse
        {
            ValidItems = validItems,
            InvalidItems = invalidItems,
            Subtotal = subtotal,
            DiscountAmount = 0,
            FinalTotal = finalTotal,
            CanProceed = validItems.Count > 0,
            BlockingMessages = blockingMessages
        };
    }

    public async Task<CreatePaymentResponse> CreatePaymentAsync(
        List<Guid> selectedCartItemIds,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        if (selectedCartItemIds == null || selectedCartItemIds.Count == 0)
        {
            throw new ArgumentException("At least one cart item must be selected.");
        }

        var idSet = selectedCartItemIds.ToHashSet();

        var cartItems = await _dbContext.CartItems
            .AsNoTracking()
            .Include(ci => ci.Cart)
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.Discount)
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.SteamKeys)
            .Where(ci => ci.Cart!.UserId == userId && idSet.Contains(ci.Id))
            .ToListAsync(cancellationToken);

        if (cartItems.Count == 0)
        {
            throw new ArgumentException("No valid cart items found for the selected IDs.");
        }

        var now = DateTime.UtcNow;
        decimal subtotal = 0;
        var orderItems = new List<OrderItems>();

        foreach (var ci in cartItems)
        {
            if (ci.Product == null || ci.Product.IsDeleted)
            {
                throw new InvalidOperationException($"Product '{ci.Product?.Name ?? "Unknown"}' is no longer available.");
            }

            // Compute actual available stock from Steam keys (canonical source)
            var computedStock = ci.Product.SteamKeys
                .Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null);

            if (computedStock < ci.Quantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for '{ci.Product.Name}': {computedStock} available, {ci.Quantity} requested.");
            }

            var isActiveDiscount = ci.Product.Discount != null
                && ci.Product.Discount.StartDate <= now
                && ci.Product.Discount.EndDate >= now;

            var unitPrice = isActiveDiscount
                ? Math.Round(ci.Product.Price * (1 - ci.Product.Discount!.Percentage / 100m), 0, MidpointRounding.AwayFromZero)
                : ci.Product.Price;

            var lineTotal = Math.Round(unitPrice * ci.Quantity, 0, MidpointRounding.AwayFromZero);
            subtotal += lineTotal;

            orderItems.Add(new OrderItems
            {
                Id = Guid.NewGuid(),
                ProductId = ci.ProductId,
                UnitPrice = unitPrice,
                LineTotal = lineTotal,
                Quantity = ci.Quantity,
                SourceCartItemId = ci.Id
            });
        }

        var orderId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        var payOSClientId = _configuration["PayOS:ClientId"];
        var payOSApiKey = _configuration["PayOS:ApiKey"];
        var payOSChecksumKey = _configuration["PayOS:ChecksumKey"];
        var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";

        if (string.IsNullOrWhiteSpace(payOSClientId)
            || string.IsNullOrWhiteSpace(payOSApiKey)
            || string.IsNullOrWhiteSpace(payOSChecksumKey))
        {
            throw new InvalidOperationException(
                "PayOS configuration is incomplete. Please set PayOS:ClientId, PayOS:ApiKey, and PayOS:ChecksumKey.");
        }

        var payOSClient = new PayOSClient(new PayOSOptions
        {
            ClientId = payOSClientId!,
            ApiKey = payOSApiKey!,
            ChecksumKey = payOSChecksumKey!
        });

        var payOSOrderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var itemNameSnippets = cartItems
            .Take(3)
            .Select(ci => ci.Product?.Name ?? "Product")
            .ToList();
        var itemDescription = itemNameSnippets.Count < cartItems.Count
            ? $"{string.Join(", ", itemNameSnippets)} +{cartItems.Count - 3} more"
            : string.Join(", ", itemNameSnippets);

        var returnUrl = $"{frontendBaseUrl}/payment/return";
        var cancelUrl = $"{frontendBaseUrl}/checkout/payment-cancel";

        var paymentItems = cartItems.Select(ci =>
        {
            var isActiveDiscount = ci.Product?.Discount != null
                && ci.Product.Discount.StartDate <= now
                && ci.Product.Discount.EndDate >= now;

            var unitPrice = isActiveDiscount
                ? Math.Round((ci.Product!.Price * (1 - ci.Product.Discount!.Percentage / 100m)), 0, MidpointRounding.AwayFromZero)
                : ci.Product!.Price;

            return new PaymentLinkItem
            {
                Name = ci.Product!.Name,
                Price = (long)unitPrice,
                Quantity = ci.Quantity
            };
        }).ToList();

        var payOSRequest = new CreatePaymentLinkRequest
        {
            OrderCode = payOSOrderCode,
            Amount = (long)subtotal,
            Description = itemDescription,
            ReturnUrl = returnUrl,
            CancelUrl = cancelUrl,
            Items = paymentItems
        };

        CreatePaymentLinkResponse payOSResponse;
        try
        {
            payOSResponse = await payOSClient.PaymentRequests.CreateAsync(payOSRequest, null!);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PayOS payment link creation failed: {ex.Message}");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        var order = new Orders
        {
            Id = orderId,
            UserId = userId,
            Subtotal = subtotal,
            Status = 2,
            CreatedAt = DateTime.UtcNow,
            DiscountAmount = 0,
            FinalTotal = subtotal,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var item in orderItems)
        {
            item.OrderId = orderId;
        }

        var paymentTransaction = new PaymentTransactions
        {
            Id = paymentTransactionId,
            OrderId = orderId,
            ProviderOrderCode = payOSOrderCode.ToString(),
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            CheckoutUrl = payOSResponse.CheckoutUrl,
            Provider = 1,
            ProviderTransactionId = string.Empty,
            QrCodeUrl = payOSResponse.QrCode,
            RawRequestPayload = string.Empty,
            RawResponsePayload = System.Text.Json.JsonSerializer.Serialize(payOSResponse),
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Orders.Add(order);
        _dbContext.OrderItems.AddRange(orderItems);
        _dbContext.PaymentTransactions.Add(paymentTransaction);

        // NOTE: Do NOT decrement Products.Stock here.
        // Stock is derived from available Steam keys and is updated when keys are actually
        // consumed during fulfillment in ProcessPaymentReturnAsync.

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreatePaymentResponse
        {
            OrderId = orderId,
            PaymentTransactionId = paymentTransactionId,
            PayOSOrderCode = payOSOrderCode.ToString(),
            CheckoutUrl = payOSResponse.CheckoutUrl,
            QrCodeUrl = payOSResponse.QrCode,
            Status = "Pending",
            ExpiresAt = expiresAt
        };
    }

    public async Task<PaymentReturnResponse> ProcessPaymentReturnAsync(
        string orderCode,
        string status,
        bool success,
        string? transactionId,
        decimal? amountPaid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Order code is required.",
                OrderCode = string.Empty,
                Status = "error"
            };
        }

        var pt = await _dbContext.PaymentTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProviderOrderCode == orderCode, cancellationToken);

        if (pt == null)
        {
            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Payment transaction not found.",
                OrderCode = orderCode,
                Status = "not_found"
            };
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Include(o => o.SteamKeys)
            .FirstOrDefaultAsync(o => o.Id == pt.OrderId, cancellationToken);

        if (order == null)
        {
            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Order not found.",
                OrderCode = orderCode,
                Status = "not_found"
            };
        }

        // Query PayOS directly for the authoritative payment status.
        // Do NOT trust the query-string `success` / `status` params — PayOS can send
        // mismatched values (e.g. success=false with status=PAID).
        var payOSClientId = _configuration["PayOS:ClientId"];
        var payOSApiKey = _configuration["PayOS:ApiKey"];
        var payOSChecksumKey = _configuration["PayOS:ChecksumKey"];

        if (string.IsNullOrWhiteSpace(payOSClientId)
            || string.IsNullOrWhiteSpace(payOSApiKey)
            || string.IsNullOrWhiteSpace(payOSChecksumKey))
        {
            return new PaymentReturnResponse
            {
                Success = false,
                Message = "PayOS configuration is missing. Cannot verify payment status.",
                OrderCode = orderCode,
                Status = "error"
            };
        }

        var payOSClient = new PayOSClient(new PayOSOptions
        {
            ClientId = payOSClientId,
            ApiKey = payOSApiKey,
            ChecksumKey = payOSChecksumKey
        });

        PaymentLink payOSData;
        try
        {
            payOSData = await payOSClient.PaymentRequests.GetAsync(long.Parse(orderCode), new RequestOptions
            {
                CancellationToken = cancellationToken
            });
        }
        catch (Exception ex)
        {
            return new PaymentReturnResponse
            {
                Success = false,
                Message = $"Failed to retrieve payment status from PayOS: {ex.Message}",
                OrderCode = orderCode,
                Status = "error"
            };
        }

        // Determine outcome from PayOS-authoritative status only.
        bool isPaidSuccess = payOSData.Status == PaymentLinkStatus.Paid;
        bool isCancelled  = payOSData.Status == PaymentLinkStatus.Cancelled;

        if (isPaidSuccess)
        {
            order.Status = 4;
            order.FulfilledAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;

            pt.Status = 2;
            pt.ProviderTransactionId =
                (payOSData.Transactions?.FirstOrDefault() as PaymentTransaction)?.Reference
                ?? transactionId
                ?? string.Empty;
            pt.UpdatedAt = DateTime.UtcNow;

            foreach (var orderItem in order.OrderItems)
            {
                for (int i = 0; i < orderItem.Quantity; i++)
                {
                    var availableKey = await _dbContext.SteamKeys
                        .FirstOrDefaultAsync(sk =>
                            sk.ProductId == orderItem.ProductId &&
                            sk.OrderId == null &&
                            sk.Status == 0 &&
                            sk.InvalidatedAt == null,
                        cancellationToken);

                    if (availableKey != null)
                    {
                        availableKey.OrderId = order.Id;
                        availableKey.Status = 1;
                        availableKey.UpdatedAt = DateTime.UtcNow;
                        break;
                    }
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Sync Products.Stock for all affected products after key consumption
            var affectedProductIds = order.OrderItems.Select(oi => oi.ProductId).Distinct().ToList();
            foreach (var productId in affectedProductIds)
            {
                await SyncProductStockAsync(productId, cancellationToken);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new PaymentReturnResponse
            {
                Success = true,
                Message = "Payment successful. Your order is complete.",
                OrderCode = orderCode,
                Status = "PAID"
            };
        }

        if (isCancelled)
        {
            order.Status = 0;
            pt.Status = 3;
            order.UpdatedAt = DateTime.UtcNow;
            pt.UpdatedAt = DateTime.UtcNow;

            // NOTE: Products.Stock was never decremented during payment creation,
            // so no restoration needed here. Keys were never consumed.

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Payment was cancelled. Items have been restocked.",
                OrderCode = orderCode,
                Status = "CANCELLED"
            };
        }

        // All other PayOS statuses = payment failed.
        order.Status = 1;
        pt.Status = 0;
        order.UpdatedAt = DateTime.UtcNow;
        pt.UpdatedAt = DateTime.UtcNow;

        // NOTE: Products.Stock was never decremented during payment creation,
        // so no restoration needed here. Keys were never consumed.

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PaymentReturnResponse
        {
            Success = false,
            Message = "Payment failed. Please try again or contact support if money was deducted.",
            OrderCode = orderCode,
            Status = "FAILED"
        };
    }

    private static CheckoutPreviewResponse EmptyPreview() => new()
    {
        ValidItems = new List<CheckoutPreviewItemResponse>(),
        InvalidItems = new List<CheckoutInvalidItemResponse>(),
        Subtotal = 0,
        DiscountAmount = 0,
        FinalTotal = 0,
        CanProceed = false,
        BlockingMessages = new List<string>()
    };

    private async Task SyncProductStockAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var availableCount = await _dbContext.SteamKeys
            .CountAsync(sk => sk.ProductId == productId && sk.Status == 0, cancellationToken);

        var product = await _dbContext.Products.FindAsync(new object[] { productId }, cancellationToken);
        if (product != null && product.Stock != availableCount)
        {
            _logger?.LogWarning(
                "[CheckoutService.StockSync] ProductId={ProductId} OldStock={OldStock} NewStock={NewStock}",
                productId, product.Stock, availableCount);
            product.Stock = availableCount;
        }
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
