using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class CloudinaryImageUploadService : IImageUploadService
{
    private readonly Cloudinary _cloudinary;
    private readonly long _maxFileSizeBytes;
    private readonly ILogger<CloudinaryImageUploadService>? _logger;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    private const long OneByte = 1;

    public CloudinaryImageUploadService(
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryImageUploadService>? logger = null)
    {
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.CloudName) ||
            string.IsNullOrWhiteSpace(opts.ApiKey) ||
            string.IsNullOrWhiteSpace(opts.ApiSecret))
        {
            throw new InvalidOperationException(
                "Cloudinary configuration is incomplete. Ensure CloudName, ApiKey, and ApiSecret are set.");
        }

        var account = new Account(opts.CloudName, opts.ApiKey, opts.ApiSecret);
        _cloudinary = new Cloudinary(account);
        _maxFileSizeBytes = opts.MaxFileSizeBytes > 0 ? opts.MaxFileSizeBytes : 10 * 1024 * 1024;
        _logger = logger;
    }

    public async Task<ImageUploadResultDto> UploadImageAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[ImageUpload] Upload started: {FileName}, {ContentType}, {Size}",
            file.FileName, file.ContentType, file.Length);

        ValidateFile(file);

        var publicId = $"hotwatergas/products/{Guid.NewGuid()}";

        await using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(publicId, stream),
            PublicId = publicId,
            Overwrite = false,
            UseFilename = false,
            UniqueFilename = false,
            Folder = "hotwatergas/products"
        };

        var result = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (result.Error != null)
        {
            _logger?.LogError("[ImageUpload] Cloudinary error: {Error}", result.Error.Message);
            throw new InvalidOperationException($"Image upload failed: {result.Error.Message}");
        }

        _logger?.LogInformation(
            "[ImageUpload] Upload succeeded: {PublicId} -> {Url}",
            result.PublicId, result.SecureUrl);

        return new ImageUploadResultDto
        {
            Url = result.SecureUrl.ToString(),
            PublicId = result.PublicId
        };
    }

    private void ValidateFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("No file provided or file is empty.");
        }

        if (file.Length > _maxFileSizeBytes)
        {
            throw new ArgumentException(
                $"File size exceeds the maximum allowed size of {_maxFileSizeBytes / (OneByte * 1024 * 1024)} MB.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            throw new ArgumentException(
                $"Invalid file type '{file.ContentType}'. Allowed types: image/png, image/jpeg, image/webp.");
        }
    }
}
