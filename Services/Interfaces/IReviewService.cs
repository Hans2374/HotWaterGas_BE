using Services.DTOs;

namespace Services.Interfaces;

public interface IReviewService
{
    Task<ReviewListResponse> GetProductReviewsAsync(Guid productId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ReviewItemResponse> CreateReviewAsync(Guid productId, int rating, string comment, CancellationToken cancellationToken = default);
    Task<ReviewItemResponse> UpdateMyReviewAsync(Guid productId, int rating, string comment, CancellationToken cancellationToken = default);
}
