using Microsoft.EntityFrameworkCore;
using Repos.Models;
using Services.DTOs;
using Services.Implementations;
using Services.Interfaces;

namespace Services.Implementations;

public class ReviewService : IReviewService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ReviewService(HotWaterGasDBContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    private Guid RequireUserId()
    {
        return _currentUserService.UserId
            ?? throw new ApiException(401, "Yêu cầu xác thực.");
    }

    public async Task<ReviewListResponse> GetProductReviewsAsync(Guid productId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Authentication is not required to read reviews — only for the "IsMine" flag.
        var currentUserId = _currentUserService.UserId;
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 50);

        var reviewsQuery = _dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId)
            .Include(r => r.User)
            .AsQueryable();

        var totalReviews = await reviewsQuery.CountAsync(cancellationToken);
        var averageRating = totalReviews > 0
            ? (decimal)Math.Round(await reviewsQuery.AverageAsync(r => r.Rating, cancellationToken), 1)
            : 0;

        var reviews = await reviewsQuery
            .OrderByDescending(r => r.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var reviewResponses = reviews.Select(r => new ReviewItemResponse
        {
            Id = r.Id,
            UserDisplayName = GetDisplayNameOrFallback(r.User),
            Rating = r.Rating,
            Comment = r.Comment,
            CreatedAt = r.CreatedAt,
            IsEdited = r.IsEdited,
            IsMine = currentUserId.HasValue && r.UserId == currentUserId.Value
        }).ToList();

        var totalPages = totalReviews == 0 ? 0 : (int)Math.Ceiling(totalReviews / (double)safePageSize);

        return new ReviewListResponse
        {
            AverageRating = averageRating,
            TotalReviews = totalReviews,
            TotalPages = totalPages,
            Reviews = reviewResponses
        };
    }

    public async Task<ReviewItemResponse> CreateReviewAsync(Guid productId, int rating, string comment, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, cancellationToken);

        if (product is null)
        {
            throw new KeyNotFoundException("Sản phẩm không tồn tại.");
        }

        var existingReview = await _dbContext.Reviews
            .FirstOrDefaultAsync(r => r.UserId == userId && r.ProductId == productId, cancellationToken);

        if (existingReview is not null)
        {
            throw new InvalidOperationException("Bạn đã đánh giá sản phẩm này rồi.");
        }

        var now = DateTime.UtcNow;
        var review = new Reviews
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = productId,
            Rating = rating,
            Comment = comment,
            CreatedAt = now,
            UpdatedAt = now,
            IsEdited = false
        };

        _dbContext.Reviews.Add(review);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return new ReviewItemResponse
        {
            Id = review.Id,
            UserDisplayName = GetDisplayNameOrFallback(user),
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            IsEdited = review.IsEdited,
            IsMine = true
        };
    }

    public async Task<ReviewItemResponse> UpdateMyReviewAsync(Guid productId, int rating, string comment, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var review = await _dbContext.Reviews
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.UserId == userId && r.ProductId == productId, cancellationToken);

        if (review is null)
        {
            throw new KeyNotFoundException("Đánh giá không tìm thấy.");
        }

        review.Rating = rating;
        review.Comment = comment;
        review.UpdatedAt = DateTime.UtcNow;
        review.IsEdited = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ReviewItemResponse
        {
            Id = review.Id,
            UserDisplayName = GetDisplayNameOrFallback(review.User),
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            IsEdited = review.IsEdited,
            IsMine = true
        };
    }

    private static string GetDisplayNameOrFallback(Users? user)
    {
        if (user == null)
            return "Người dùng";

        if (!string.IsNullOrEmpty(user.DisplayName))
            return user.DisplayName;

        var emailPrefix = user.Email?.Split('@')[0];
        return string.IsNullOrEmpty(emailPrefix) ? "Người dùng" : emailPrefix;
    }
}
