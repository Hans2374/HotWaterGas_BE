using Services.DTOs;

namespace Services.Interfaces;

public interface IWishlistService
{
    Task<WishlistResponse> GetWishlistAsync(CancellationToken cancellationToken = default);
    Task<WishlistItemResponse> AddToWishlistAsync(Guid productId, CancellationToken cancellationToken = default);
    Task RemoveFromWishlistAsync(Guid productId, CancellationToken cancellationToken = default);
}
