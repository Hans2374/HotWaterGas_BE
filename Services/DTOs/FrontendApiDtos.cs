namespace Services.DTOs;

public class ReviewListResponse
{
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public int TotalPages { get; set; }
    public List<ReviewItemResponse> Reviews { get; set; } = new();
}

public class ReviewItemResponse
{
    public Guid Id { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsMine { get; set; }
}

public class CreateReviewRequest
{
    public Guid ProductId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public class UpdateReviewRequest
{
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public class WishlistResponse
{
    public List<WishlistItemResponse> Items { get; set; } = new();
}

public class WishlistItemResponse
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string HeaderImageUrl { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountPrice { get; set; }
    public bool HasDiscount { get; set; }
    public bool InStock { get; set; }
    public DateTime AddedAt { get; set; }
}

public class CartResponse
{
    public Guid CartId { get; set; }
    public List<CartItemResponse> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal TotalAmount { get; set; }
}

public class CartItemResponse
{
    public Guid CartItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string ProductImageUrl { get; set; } = string.Empty;
    public decimal FinalPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }
    public bool InStock { get; set; }
}

public class AddToCartRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class UpdateCartQuantityRequest
{
    public int Quantity { get; set; }
}

public class MyOrderListItemResponse
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
}

public class MyOrderDetailResponse
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalTotal { get; set; }
    public string PaymentMethodLabel { get; set; } = string.Empty;
    public List<MyOrderDetailItemResponse> Items { get; set; } = new();
    public List<MyOrderLicenseResponse> Licenses { get; set; } = new();
}

public class MyOrderDetailItemResponse
{
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string ProductImageUrl { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class MyOrderLicenseResponse
{
    public string ProductName { get; set; } = string.Empty;
    public string KeyValue { get; set; } = string.Empty;
    public string RedemptionGuideUrl { get; set; } = string.Empty;
}

public class AdminProductListItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal FinalPrice { get; set; }
    public decimal? DiscountPrice { get; set; }
    public bool HasDiscount { get; set; }
    public int Stock { get; set; }
    public int AvailableSteamKeyCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string PrimaryImageUrl { get; set; } = string.Empty;
}

public class AdminProductDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public Guid? DiscountId { get; set; }
    public int Stock { get; set; }
    public AdminProductMetadataResponse Metadata { get; set; } = new();
    public AdminProductSystemRequirementsResponse SystemRequirements { get; set; } = new();
    public List<AdminProductImageResponse> Images { get; set; } = new();
    public List<ProductLookupResponse> Categories { get; set; } = new();
    public List<ProductLookupResponse> Tags { get; set; } = new();
}

public class AdminProductMetadataResponse
{
    public string Publisher { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string Platform { get; set; } = string.Empty;
}

public class AdminProductImageResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class AdminProductSystemRequirementsResponse
{
    public ProductRequirementBlockResponse Minimum { get; set; } = new();
    public ProductRequirementBlockResponse Recommended { get; set; } = new();
}

public class AdminProductUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public Guid? DiscountId { get; set; }
    public List<AdminProductImageUpsertItem> Images { get; set; } = new();
    public List<string> ImageUrls { get; set; } = new();
    public List<Guid> CategoryIds { get; set; } = new();
    public Guid? CategoryId { get; set; }
    public List<Guid> TagIds { get; set; } = new();
    public AdminProductMetadataRequest Metadata { get; set; } = new();
    public AdminProductSystemRequirementsRequest SystemRequirements { get; set; } = new();
}

public class AdminProductImageUpsertItem
{
    public Guid? Id { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class AdminProductMetadataRequest
{
    public string Publisher { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string Platform { get; set; } = string.Empty;
}

public class AdminProductSystemRequirementsRequest
{
    public ProductRequirementBlockResponse Minimum { get; set; } = new();
    public ProductRequirementBlockResponse Recommended { get; set; } = new();
}

public class PagedAdminProductListResponse
{
    public List<AdminProductListItemResponse> Data { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class AdminProductQueryRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? StockStatus { get; set; }
    public Guid? CategoryId { get; set; }
}

// ── Checkout ──────────────────────────────────────────────────────────────────

public class CheckoutPreviewItemResponse
{
    public Guid CartItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string ProductImage { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal? DiscountPrice { get; set; }
    public bool HasDiscount { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class CheckoutInvalidItemResponse
{
    public Guid CartItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class CheckoutPreviewResponse
{
    public List<CheckoutPreviewItemResponse> ValidItems { get; set; } = new();
    public List<CheckoutInvalidItemResponse> InvalidItems { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalTotal { get; set; }
    public bool CanProceed { get; set; }
    public List<string> BlockingMessages { get; set; } = new();
}

// ── Payment ───────────────────────────────────────────────────────────────────

public class CreatePaymentRequest
{
    public List<Guid> SelectedCartItemIds { get; set; } = new();
}

public class CreatePaymentResponse
{
    public Guid OrderId { get; set; }
    public Guid PaymentTransactionId { get; set; }
    public string PayOSOrderCode { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public string? QrCodeUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}

public class PaymentReturnResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}