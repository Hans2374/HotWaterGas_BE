using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayOS;
using PayOS.Models;
using PayOS.Models.V2.PaymentRequests;
using Repos.Models;
using Services.DTOs;
using Services.Implementations;
using Services.Interfaces;

namespace Services.Implementations;

public class CheckoutService : ICheckoutService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly ILogger<CheckoutService>? _logger;

    public CheckoutService(
        HotWaterGasDBContext dbContext,
        ICurrentUserService currentUserService,
        IConfiguration configuration,
        IEmailService emailService,
        ILogger<CheckoutService>? logger = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _configuration = configuration;
        _emailService = emailService;
        _logger = logger;
    }

    private Guid RequireUserId()
    {
        return _currentUserService.UserId
            ?? throw new ApiException(401, "Yêu cầu xác thực.");
    }

    public async Task<CheckoutPreviewResponse> PreviewCheckoutAsync(
        List<Guid> cartItemIds,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

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
                    ProductName = "Sản phẩm không xác định",
                    ReasonCode = "PRODUCT_NOT_FOUND",
                    Message = "Sản phẩm này không còn khả dụng."
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
                    Message = $"Chỉ còn {computedStock} sản phẩm trong kho. Bạn yêu cầu: {ci.Quantity}."
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
                ProductName = "Sản phẩm không xác định",
                ReasonCode = "CART_ITEM_NOT_FOUND",
                Message = "Sản phẩm này không có trong giỏ hàng của bạn."
            });
        }

        if (validItems.Count == 0 && invalidItems.Count > 0)
        {
            blockingMessages.Add("Không có sản phẩm nào có thể thanh toán. Vui lòng xóa các sản phẩm không khả dụng.");
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
        var userId = RequireUserId();

        if (selectedCartItemIds == null || selectedCartItemIds.Count == 0)
        {
            throw new ArgumentException("Vui lòng chọn ít nhất một sản phẩm để thanh toán.");
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
            throw new ArgumentException("Không tìm thấy sản phẩm nào trong giỏ hàng.");
        }

        var existingPendingTx = await _dbContext.PaymentTransactions
            .Where(pt => pt.Provider == 1 && pt.Status == 1)
            .Where(pt => _dbContext.OrderItems.Any(oi => oi.OrderId == pt.OrderId
                && idSet.Contains(oi.SourceCartItemId!.Value)))
            .Include(pt => pt.Order)
            .Where(pt => pt.Order != null && pt.Order!.UserId == userId)
            .OrderByDescending(pt => pt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingPendingTx != null)
        {
            var orderItemsCount = await _dbContext.OrderItems
                .CountAsync(oi => oi.OrderId == existingPendingTx.OrderId, cancellationToken);

            if (orderItemsCount == cartItems.Count)
            {
                _logger?.LogInformation(
                    "[CheckoutService.CreatePayment] Reusing existing pending payment. OrderId={OrderId}, TxId={TxId}",
                    existingPendingTx.OrderId, existingPendingTx.Id);

                var checkoutUrl = await _dbContext.PaymentTransactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(pt => pt.Id == existingPendingTx.Id, cancellationToken);

                return new CreatePaymentResponse
                {
                    OrderId = existingPendingTx.OrderId,
                    PaymentTransactionId = existingPendingTx.Id,
                    PayOSOrderCode = existingPendingTx.ProviderOrderCode,
                    CheckoutUrl = checkoutUrl?.CheckoutUrl ?? string.Empty,
                    QrCodeUrl = checkoutUrl?.QrCodeUrl,
                    Status = "Pending",
                    ExpiresAt = null
                };
            }
        }

        var now = DateTime.UtcNow;
        decimal subtotal = 0;
        var orderItems = new List<OrderItems>();

        foreach (var ci in cartItems)
        {
            if (ci.Product == null || ci.Product.IsDeleted)
            {
                throw new InvalidOperationException($"Sản phẩm '{ci.Product?.Name ?? "không xác định"}' không còn khả dụng.");
            }

            var computedStock = ci.Product.SteamKeys
                .Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null);

            if (computedStock < ci.Quantity)
            {
                throw new InvalidOperationException(
                    $"Sản phẩm '{ci.Product.Name}': chỉ còn {computedStock} trong kho, yêu cầu {ci.Quantity}.");
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
                "Cấu hình PayOS không đầy đủ. Vui lòng thiết lập PayOS:ClientId, PayOS:ApiKey và PayOS:ChecksumKey.");
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
            .Select(ci => ci.Product?.Name ?? "Sản phẩm")
            .ToList();
        var itemDescription = itemNameSnippets.Count < cartItems.Count
            ? $"{string.Join(", ", itemNameSnippets)} +{cartItems.Count - 3} sản phẩm khác"
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
            throw new InvalidOperationException($"Tạo liên kết thanh toán PayOS thất bại: {ex.Message}");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        var order = new Orders
        {
            Id = orderId,
            UserId = userId,
            CartId = cartItems.Count > 0 && cartItems[0].Cart != null ? cartItems[0].Cart.Id : null,
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

        if (cartItems.Count > 0 && cartItems[0].Cart != null)
        {
            var cartId = cartItems[0].Cart.Id;
            var trackedCart = await _dbContext.Carts.FindAsync(new object[] { cartId }, cancellationToken);
            if (trackedCart != null)
            {
                trackedCart.IsCheckedOut = true;
            }
        }

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
        // This is the PayOS webhook/callback — no JWT authentication required.
        // The service is authenticated via PayOS's signature verification on their end.

        _logger?.LogInformation(
            "[CheckoutService.PaymentReturn] ENTRY. orderCode={orderCode}, status={status}, success={success}, transactionId={transactionId}",
            orderCode, status, success, transactionId);

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

        var trackedPt = await _dbContext.PaymentTransactions
            .FirstOrDefaultAsync(p => p.ProviderOrderCode == orderCode, cancellationToken);

        if (trackedPt == null)
        {
            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Không tìm thấy giao dịch thanh toán.",
                OrderCode = orderCode,
                Status = "not_found"
            };
        }

        var trackedOrder = await _dbContext.Orders
            .Include(o => o.User)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
                    .ThenInclude(p => p!.ProductImages)
            .Include(o => o.SteamKeys)
                .ThenInclude(sk => sk.Product)
                    .ThenInclude(p => p!.ProductImages)
            .FirstOrDefaultAsync(o => o.Id == trackedPt.OrderId, cancellationToken);

        if (trackedOrder == null)
        {
            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Không tìm thấy đơn hàng.",
                OrderCode = orderCode,
                Status = "not_found"
            };
        }

        if (trackedOrder.Status == 4)
        {
            _logger?.LogInformation(
                "[CheckoutService.PaymentReturn] Idempotent skip for already-completed order. OrderId={OrderId}",
                trackedOrder.Id);
            return new PaymentReturnResponse
            {
                Success = true,
                Message = "Đơn hàng đã được xử lý trước đó.",
                OrderCode = orderCode,
                Status = "PAID"
            };
        }

        if (trackedOrder.Status == 0)
        {
            _logger?.LogInformation(
                "[CheckoutService.PaymentReturn] Idempotent skip for already-cancelled order. OrderId={OrderId}",
                trackedOrder.Id);
            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Đơn hàng đã bị hủy trước đó.",
                OrderCode = orderCode,
                Status = "CANCELLED"
            };
        }

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
                Message = "Cấu hình PayOS không đầy đủ. Không thể xác minh thanh toán.",
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
                Message = $"Không thể xác minh thanh toán từ PayOS: {ex.Message}",
                OrderCode = orderCode,
                Status = "error"
            };
        }

        bool isPaidSuccess = payOSData.Status == PaymentLinkStatus.Paid;
        bool isCancelled = payOSData.Status == PaymentLinkStatus.Cancelled;

        if (isPaidSuccess)
        {
            _logger?.LogInformation(
                "[CheckoutService.PaymentReturn] PAID branch. OrderId={OrderId}, ProviderOrderCode={OrderCode}",
                trackedOrder.Id, orderCode);

            trackedOrder.Status = 4;
            trackedOrder.FulfilledAt = DateTime.UtcNow;
            trackedOrder.UpdatedAt = DateTime.UtcNow;

            trackedPt.Status = 2;
            trackedPt.ProviderTransactionId =
                (payOSData.Transactions?.FirstOrDefault() as PaymentTransaction)?.Reference
                ?? transactionId
                ?? string.Empty;
            trackedPt.UpdatedAt = DateTime.UtcNow;

            foreach (var orderItem in trackedOrder.OrderItems)
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
                        availableKey.OrderId = trackedOrder.Id;
                        availableKey.Status = 2;
                        availableKey.UpdatedAt = DateTime.UtcNow;
                        break;
                    }
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var affectedProductIds = trackedOrder.OrderItems.Select(oi => oi.ProductId).Distinct().ToList();
            foreach (var productId in affectedProductIds)
            {
                await SyncProductStockAsync(productId, cancellationToken);
            }

            var cartItemIdsToRemove = trackedOrder.OrderItems
                .Where(oi => oi.SourceCartItemId.HasValue)
                .Select(oi => oi.SourceCartItemId!.Value)
                .ToList();

            if (cartItemIdsToRemove.Count > 0)
            {
                var cartItemsToRemove = await _dbContext.CartItems
                    .Where(ci => cartItemIdsToRemove.Contains(ci.Id))
                    .ToListAsync(cancellationToken);

                if (cartItemsToRemove.Count > 0)
                {
                    _dbContext.CartItems.RemoveRange(cartItemsToRemove);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger?.LogInformation(
                "[CheckoutService.PaymentReturn] PAID save complete. OrderId={OrderId}",
                trackedOrder.Id);

            if (!trackedOrder.FulfillmentEmailSentAt.HasValue)
            {
                await SendFulfillmentEmailAsync(trackedOrder, trackedPt.ProviderOrderCode, cancellationToken);
            }

            return new PaymentReturnResponse
            {
                Success = true,
                Message = "Thanh toán thành công. Đơn hàng của bạn đã hoàn tất.",
                OrderCode = orderCode,
                Status = "PAID"
            };
        }

        if (isCancelled)
        {
            _logger?.LogInformation(
                "[CheckoutService.PaymentReturn] CANCELLED branch. OrderId={OrderId}, ProviderOrderCode={OrderCode}",
                trackedOrder.Id, orderCode);

            trackedOrder.Status = 0;
            trackedPt.Status = 3;
            trackedOrder.UpdatedAt = DateTime.UtcNow;
            trackedPt.UpdatedAt = DateTime.UtcNow;

            if (trackedOrder.CartId.HasValue)
            {
                var trackedCart = await _dbContext.Carts
                    .FirstOrDefaultAsync(c => c.Id == trackedOrder.CartId.Value, cancellationToken);
                if (trackedCart != null)
                {
                    trackedCart.IsCheckedOut = false;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new PaymentReturnResponse
            {
                Success = false,
                Message = "Thanh toán đã bị hủy. Các sản phẩm đã được hoàn lại vào giỏ hàng.",
                OrderCode = orderCode,
                Status = "CANCELLED"
            };
        }

        _logger?.LogInformation(
            "[CheckoutService.PaymentReturn] FAILED branch. OrderId={OrderId}, ProviderOrderCode={OrderCode}, PayOSStatus={PayOSStatus}",
            trackedOrder.Id, orderCode, payOSData.Status);

        trackedOrder.Status = 1;
        trackedPt.Status = 0;
        trackedOrder.UpdatedAt = DateTime.UtcNow;
        trackedPt.UpdatedAt = DateTime.UtcNow;

        if (trackedOrder.CartId.HasValue)
        {
            var failedCart = await _dbContext.Carts
                .FirstOrDefaultAsync(c => c.Id == trackedOrder.CartId.Value, cancellationToken);
            if (failedCart != null)
            {
                failedCart.IsCheckedOut = false;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PaymentReturnResponse
        {
            Success = false,
            Message = "Thanh toán thất bại. Vui lòng thử lại hoặc liên hệ hỗ trợ nếu đã bị trừ tiền.",
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

    private async Task SendFulfillmentEmailAsync(
        Orders order,
        string orderCode,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "[CheckoutService.SendFulfillmentEmail] Starting. OrderId={OrderId}, UserId={UserId}",
            order.Id, order.UserId);

        var toEmail = order.User?.Email;
        var toName = order.User?.DisplayName ?? string.Empty;

        if (string.IsNullOrWhiteSpace(toEmail))
        {
            _logger?.LogWarning(
                "[CheckoutService.SendFulfillmentEmail] No email address found for UserId={UserId}. Skipping email.",
                order.UserId);
            return;
        }

        var steamKeysByProduct = order.SteamKeys
            .Where(sk => sk.Product != null && !string.IsNullOrWhiteSpace(sk.KeyValue))
            .GroupBy(sk => sk.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.ToList()
            );

        var imagesByProduct = order.SteamKeys
            .Where(sk => sk.Product?.ProductImages != null)
            .SelectMany(sk => sk.Product!.ProductImages
                .Select(img => new { img.ProductId, img.IsPrimary, img.DisplayOrder, img.ImageUrl }))
            .GroupBy(x => x.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.DisplayOrder)
                    .First()
                    .ImageUrl
            );

        var items = order.OrderItems.Select(oi =>
        {
            steamKeysByProduct.TryGetValue(oi.ProductId, out var keysForProduct);
            imagesByProduct.TryGetValue(oi.ProductId, out var imageUrl);

            return new FulfillmentOrderItem
            {
                ProductId = oi.ProductId,
                ProductName = oi.Product?.Name ?? "Sản phẩm không xác định",
                ProductImageUrl = imageUrl,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                LineTotal = oi.LineTotal,
                SteamKeys = keysForProduct?
                    .Select(sk => sk.KeyValue)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToList() ?? new List<string>()
            };
        }).ToList();

        var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? string.Empty;
        var logoUrl = !string.IsNullOrWhiteSpace(frontendBaseUrl)
            ? $"{frontendBaseUrl.TrimEnd('/')}/icon.png"
            : string.Empty;

        var request = new FulfillmentEmailRequest
        {
            ToEmail = toEmail,
            ToName = toName,
            OrderCode = orderCode,
            OrderDate = order.CreatedAt,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            FinalTotal = order.FinalTotal,
            PaymentStatus = "PAID",
            LogoUrl = logoUrl,
            Items = items
        };

        try
        {
            await _emailService.SendFulfillmentEmailAsync(request, cancellationToken);

            order.FulfillmentEmailSentAt = DateTime.UtcNow;
            order.FulfillmentLastError = string.Empty;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger?.LogInformation(
                "[CheckoutService.SendFulfillmentEmail] Email sent successfully. OrderId={OrderId}, To={Email}",
                order.Id, toEmail);
        }
        catch (Exception ex)
        {
            order.FulfillmentLastError = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger?.LogError(
                ex,
                "[CheckoutService.SendFulfillmentEmail] Email sending failed. OrderId={OrderId}, To={Email}, Error={Error}",
                order.Id, toEmail, ex.Message);
        }
    }
}
