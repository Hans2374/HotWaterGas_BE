using Microsoft.AspNetCore.Http;
using Services.DTOs;

namespace Services.Interfaces;

public interface IImageUploadService
{
    Task<ImageUploadResultDto> UploadImageAsync(IFormFile file, CancellationToken cancellationToken = default);
}
