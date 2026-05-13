using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class WishlistService : IWishlistService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WishlistService(HotWaterGasDBContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<WishlistResponse> GetWishlistAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        var wishlist = await _dbContext.Wishlists
            .AsNoTracking()
            .Include(w => w.WishlistItems)
                .ThenInclude(wi => wi.Product)
                    .ThenInclude(p => p!.Discount)
            .Include(w => w.WishlistItems)
                .ThenInclude(wi => wi.Product)
                    .ThenInclude(p => p!.ProductImages)
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wishlist is null)
        {
            return new WishlistResponse { Items = new List<WishlistItemResponse>() };
        }

        var now = DateTime.UtcNow;
        var items = wishlist.WishlistItems
            .Where(wi => wi.Product != null && !wi.Product.IsDeleted)
            .Select(wi => MapToWishlistItemResponse(wi, now))
            .OrderByDescending(wi => wi.AddedAt)
            .ToList();

        return new WishlistResponse { Items = items };
    }

    public async Task<WishlistItemResponse> AddToWishlistAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Discount)
            .Include(p => p.ProductImages)
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Product not found.");
        }

        var wishlist = await _dbContext.Wishlists
            .Include(w => w.WishlistItems)
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wishlist is null)
        {
            wishlist = new Wishlists
            {
                Id = Guid.NewGuid(),
                UserId = userId
            };
            _dbContext.Wishlists.Add(wishlist);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var existingItem = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(wi => wi.WishlistId == wishlist.Id && wi.ProductId == productId, cancellationToken);

        if (existingItem is not null)
        {
            throw new InvalidOperationException("Product already in wishlist.");
        }

        var now = DateTime.UtcNow;
        var wishlistItem = new WishlistItems
        {
            Id = Guid.NewGuid(),
            WishlistId = wishlist.Id,
            ProductId = productId,
            CreatedAt = now
        };

        _dbContext.WishlistItems.Add(wishlistItem);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToWishlistItemResponse(new { Product = product, CreatedAt = now }, now);
    }

    public async Task RemoveFromWishlistAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        var wishlist = await _dbContext.Wishlists
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wishlist is null)
        {
            return;
        }

        var item = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(wi => wi.WishlistId == wishlist.Id && wi.ProductId == productId, cancellationToken);

        if (item is null)
        {
            throw new KeyNotFoundException("Item not found in wishlist.");
        }

        _dbContext.WishlistItems.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static WishlistItemResponse MapToWishlistItemResponse(dynamic item, DateTime now)
    {
        var product = item.Product as Products;
        var createdAt = item.CreatedAt;

        var discountPercentage = product?.Discount != null 
            && product.Discount.StartDate <= now 
            && product.Discount.EndDate >= now
            ? (decimal?)product.Discount.Percentage
            : null;

        var basePrice = product?.Price ?? 0;
        var finalPrice = discountPercentage.HasValue 
            ? Math.Round(basePrice * (1 - (discountPercentage.Value / 100m)), 0, MidpointRounding.AwayFromZero)
            : basePrice;

        var headerImageUrl = product?.ProductImages
            .OrderBy(i => i.IsPrimary ? 0 : 1)
            .ThenBy(i => i.DisplayOrder)
            .Select(i => i.ImageUrl)
            .FirstOrDefault() ?? string.Empty;

        return new WishlistItemResponse
        {
            ProductId = product?.Id ?? Guid.Empty,
            ProductName = product?.Name ?? string.Empty,
            ProductSlug = product?.Slug ?? string.Empty,
            HeaderImageUrl = headerImageUrl,
            Price = basePrice,
            DiscountPercentage = discountPercentage ?? 0,
            DiscountPrice = discountPercentage.HasValue ? finalPrice : 0,
            HasDiscount = discountPercentage.HasValue,
            IsInStock = (product?.Stock ?? 0) > 0,
            AddedAt = createdAt
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
