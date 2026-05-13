using Microsoft.AspNetCore.Http;

namespace HotWaterGas_BE.DTOs;

/// <summary>
/// DTO wrapper for file upload form data. Using a non-IFormFile type as the action
/// parameter allows Swashbuckle to generate the OpenAPI schema without throwing when
/// it encounters [FromForm] IFormFile.
/// </summary>
public class FileUploadRequest
{
    public IFormFile? File { get; set; }
}
