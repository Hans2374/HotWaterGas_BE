using HotWaterGas_BE.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/uploads")]
public class UploadController : ControllerBase
{
    private readonly IImageUploadService _imageUploadService;
    private readonly ILogger<UploadController> _logger;

    public UploadController(IImageUploadService imageUploadService, ILogger<UploadController> logger)
    {
        _imageUploadService = imageUploadService;
        _logger = logger;
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("products")]
    public async Task<IActionResult> UploadProductImage(
        [FromForm] FileUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.File == null || request.File.Length == 0)
        {
            _logger.LogWarning("[Upload] No file provided for product image");
            return BadRequest(new { message = "No file provided." });
        }

        try
        {
            var result = await _imageUploadService.UploadImageAsync(request.File, cancellationToken);

            _logger.LogInformation(
                "[Upload] Product image success: {PublicId} -> {Url}",
                result.PublicId, result.Url);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("[Upload] Validation error: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("[Upload] Upload failed: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Image upload failed. Please try again." });
        }
        catch (Exception ex)
        {
            _logger.LogError("[Upload] Unexpected error: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("images")]
    public async Task<IActionResult> UploadImage(
        [FromForm] FileUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.File == null || request.File.Length == 0)
        {
            _logger.LogWarning("[Upload] No file provided");
            return BadRequest(new { message = "No file provided." });
        }

        try
        {
            var result = await _imageUploadService.UploadImageAsync(request.File, cancellationToken);

            _logger.LogInformation(
                "[Upload] Success: {PublicId} -> {Url}",
                result.PublicId, result.Url);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("[Upload] Validation error: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("[Upload] Upload failed: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Image upload failed. Please try again." });
        }
        catch (Exception ex)
        {
            _logger.LogError("[Upload] Unexpected error: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
        }
    }
}
