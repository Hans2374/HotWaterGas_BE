using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProductReviews(
        [FromRoute] Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var response = await _reviewService.GetProductReviewsAsync(productId, page, pageSize, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateReview(
        [FromRoute] Guid productId,
        [FromBody] CreateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _reviewService.CreateReviewAsync(productId, request.Rating, request.Comment, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyReview(
        [FromRoute] Guid productId,
        [FromBody] UpdateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _reviewService.UpdateMyReviewAsync(productId, request.Rating, request.Comment, cancellationToken);
        return Ok(response);
    }
}
