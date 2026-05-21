using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Implementations;
using Services.Interfaces;

namespace Services.Implementations;

public class CartService : ICartService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public CartService(HotWaterGasDBContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    private Guid RequireUserId()
    {
        return _currentUserService.UserId
            ?? throw new ApiException(401, "Yêu cầu xác thực.");
    }

    public async Task<CartResponse> GetMyCartAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var cart = await _dbContext.Carts
            .AsNoTracking()
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p!.Discount)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p!.ProductImages)
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p!.SteamKeys)
            .FirstOrDefaultAsync(c => c.UserId == userId && !c.IsCheckedOut, cancellationToken);

        if (cart is null)
        {
            return new CartResponse
            {
                CartId = Guid.Empty,
                Items = new List<CartItemResponse>(),
                Subtotal = 0,
                TotalAmount = 0
            };
        }

        var now = DateTime.UtcNow;
        var items = cart.CartItems
            .Where(ci => ci.Product != null && !ci.Product.IsDeleted)
            .Select(ci => MapToCartItemResponse(ci, now))
            .ToList();

        var subtotal = items.Sum(i => i.Subtotal);

        return new CartResponse
        {
            CartId = cart.Id,
            Items = items,
            Subtotal = subtotal,
            TotalAmount = subtotal
        };
    }

    public async Task<CartResponse> AddToCartAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Discount)
            .Include(p => p.ProductImages)
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Sản phẩm không tồn tại.");
        }

        var cart = await _dbContext.Carts
            .Include(c => c.CartItems)
            .FirstOrDefaultAsync(c => c.UserId == userId && !c.IsCheckedOut, cancellationToken);

        if (cart is null)
        {
            cart = new Carts
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                IsCheckedOut = false,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Carts.Add(cart);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var existingItem = await _dbContext.CartItems
            .FirstOrDefaultAsync(ci => ci.CartId == cart.Id && ci.ProductId == productId, cancellationToken);

        if (existingItem is not null)
        {
            existingItem.Quantity += quantity;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var cartItem = new CartItems
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = productId,
                Quantity = quantity
            };

            _dbContext.CartItems.Add(cartItem);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetMyCartAsync(cancellationToken);
    }

    public async Task<CartItemResponse> UpdateCartItemQuantityAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var cart = await _dbContext.Carts
            .FirstOrDefaultAsync(c => c.UserId == userId && !c.IsCheckedOut, cancellationToken);

        if (cart is null)
        {
            throw new KeyNotFoundException("Giỏ hàng không tồn tại.");
        }

        var cartItem = await _dbContext.CartItems
            .FirstOrDefaultAsync(ci => ci.CartId == cart.Id && ci.ProductId == productId, cancellationToken);

        if (cartItem is null)
        {
            throw new KeyNotFoundException("Sản phẩm không có trong giỏ hàng.");
        }

        if (quantity <= 0)
        {
            _dbContext.CartItems.Remove(cartItem);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new CartItemResponse
            {
                CartItemId = cartItem.Id,
                ProductId = productId,
                ProductName = string.Empty,
                ProductSlug = string.Empty,
                ProductImageUrl = string.Empty,
                FinalPrice = 0,
                Quantity = 0,
                Subtotal = 0,
                InStock = false
            };
        }

        cartItem.Quantity = quantity;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetCartItemResponseAsync(cartItem.Id, cancellationToken);
    }

    public async Task RemoveFromCartAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var cart = await _dbContext.Carts
            .FirstOrDefaultAsync(c => c.UserId == userId && !c.IsCheckedOut, cancellationToken);

        if (cart is null)
        {
            return;
        }

        var cartItem = await _dbContext.CartItems
            .FirstOrDefaultAsync(ci => ci.CartId == cart.Id && ci.ProductId == productId, cancellationToken);

        if (cartItem is null)
        {
            throw new KeyNotFoundException("Sản phẩm không có trong giỏ hàng.");
        }

        _dbContext.CartItems.Remove(cartItem);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CartItemResponse> GetCartItemResponseAsync(Guid cartItemId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var item = await _dbContext.CartItems
            .AsNoTracking()
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.Discount)
            .Include(ci => ci.Product)
                .ThenInclude(p => p!.ProductImages)
            .FirstOrDefaultAsync(ci => ci.Id == cartItemId, cancellationToken);

        if (item is null || item.Product is null)
        {
            return new CartItemResponse
            {
                CartItemId = cartItemId,
                ProductId = Guid.Empty,
                InStock = false
            };
        }

        return MapToCartItemResponse(item, now);
    }

    private static CartItemResponse MapToCartItemResponse(CartItems item, DateTime now)
    {
        var product = item.Product;

        var discountPercentage = product?.Discount != null
            && product.Discount.StartDate <= now
            && product.Discount.EndDate >= now
            ? (decimal?)product.Discount.Percentage
            : null;

        var basePrice = product?.Price ?? 0;
        var finalPrice = discountPercentage.HasValue
            ? Math.Round(basePrice * (1 - (discountPercentage.Value / 100m)), 0, MidpointRounding.AwayFromZero)
            : basePrice;

        var imageUrl = product?.ProductImages
            .OrderBy(i => i.IsPrimary ? 0 : 1)
            .ThenBy(i => i.DisplayOrder)
            .Select(i => i.ImageUrl)
            .FirstOrDefault() ?? string.Empty;

        var computedStock = product?.SteamKeys
            .Count(sk => sk.Status == 0 && sk.OrderId == null && sk.InvalidatedAt == null) ?? 0;

        return new CartItemResponse
        {
            CartItemId = item.Id,
            ProductId = product?.Id ?? Guid.Empty,
            ProductName = product?.Name ?? string.Empty,
            ProductSlug = product?.Slug ?? string.Empty,
            ProductImageUrl = imageUrl,
            FinalPrice = finalPrice,
            Quantity = item.Quantity,
            Subtotal = finalPrice * item.Quantity,
            InStock = computedStock > 0
        };
    }
}
