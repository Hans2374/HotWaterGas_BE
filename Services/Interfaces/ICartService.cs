using Services.DTOs;

namespace Services.Interfaces;

public interface ICartService
{
    Task<CartResponse> GetMyCartAsync(CancellationToken cancellationToken = default);
    Task<CartItemResponse> AddToCartAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
    Task<CartItemResponse> UpdateCartItemQuantityAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
    Task RemoveFromCartAsync(Guid productId, CancellationToken cancellationToken = default);
}
