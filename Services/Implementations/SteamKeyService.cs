using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class SteamKeyService : ISteamKeyService
{
    private readonly HotWaterGasDBContext _dbContext;
    private readonly ILogger<SteamKeyService>? _logger;

    public SteamKeyService(HotWaterGasDBContext dbContext, ILogger<SteamKeyService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    private async Task SyncProductStockAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var availableCount = await _dbContext.SteamKeys
            .CountAsync(sk => sk.ProductId == productId && sk.Status == (int)SteamKeyStatus.Available, cancellationToken);

        var product = await _dbContext.Products.FindAsync(new object[] { productId }, cancellationToken);
        if (product != null)
        {
            product.Stock = availableCount;
        }
    }

    public async Task<SteamKeyBulkUploadResponse> BulkUploadAsync(
        Guid productId,
        List<string> keyValues,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var normalized = keyValues
            .Select(k => k.Trim().ToUpperInvariant())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct()
            .ToList();

        var invalidCount = normalized.Count(k => k.Length < 5 || k.Length > 50);

        var existingKeys = await _dbContext.SteamKeys
            .Where(sk => sk.ProductId == productId)
            .Select(sk => sk.KeyValue.ToUpperInvariant())
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);

        var toInsert = normalized
            .Where(k => k.Length >= 5 && k.Length <= 50 && !existingSet.Contains(k))
            .Select(keyValue => new SteamKeys
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                KeyValue = keyValue,
                Status = (int)SteamKeyStatus.Available,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToList();

        if (toInsert.Count > 0)
        {
            _dbContext.SteamKeys.AddRange(toInsert);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await SyncProductStockAsync(productId, cancellationToken);
        }

        var duplicateCount = normalized.Count(
            k => k.Length >= 5 && k.Length <= 50 && existingSet.Contains(k));

        _logger?.LogInformation(
            "[SteamKeys.BulkUpload] ProductId={ProductId} Inserted={Inserted} Duplicates={Duplicates} Invalid={Invalid}",
            productId, toInsert.Count, duplicateCount, invalidCount);

        return new SteamKeyBulkUploadResponse
        {
            InsertedCount = toInsert.Count,
            SkippedDuplicateCount = duplicateCount,
            InvalidRowCount = invalidCount
        };
    }

    public async Task<SteamKeyListResponse> GetKeysAsync(
        Guid productId,
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);

        var query = _dbContext.SteamKeys
            .AsNoTracking()
            .Where(sk => sk.ProductId == productId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusCode = MapFrontendStatus(status);
            if (statusCode.HasValue)
            {
                query = query.Where(sk => sk.Status == statusCode.Value);
            }
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var keys = await query
            .OrderByDescending(sk => sk.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(sk => new SteamKeyListItemResponse
            {
                Id = sk.Id,
                Key = sk.KeyValue, // Return raw key for admin UI; frontend handles masking for display
                Status = sk.Status,
                StatusName = sk.Status == 0 ? "Available" : sk.Status == 1 ? "Disabled" : sk.Status == 2 ? "Sold" : "Disabled",
                CreatedAt = sk.CreatedAt,
                UsedAt = sk.UsedAt,
                OrderId = sk.OrderId
            })
            .ToListAsync(cancellationToken);

        _logger?.LogInformation(
            "[SteamKeys.GetKeys] ProductId={ProductId} Count={Count} FirstKey={FirstKey}",
            productId, keys.Count, keys.FirstOrDefault()?.Key ?? "null");

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)safePageSize);

        return new SteamKeyListResponse
        {
            Data = keys,
            Page = safePage,
            PageSize = safePageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public async Task<SteamKeyDetailResponse?> GetKeyByIdAsync(
        Guid productId,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var key = await _dbContext.SteamKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(sk => sk.Id == keyId && sk.ProductId == productId, cancellationToken);

        if (key == null)
        {
            return null;
        }

        return new SteamKeyDetailResponse
        {
            Id = key.Id,
            ProductId = key.ProductId,
            KeyValue = key.KeyValue,
            Status = key.Status,
            StatusName = MapStatusToName(key.Status),
            CreatedAt = key.CreatedAt,
            UsedAt = key.UsedAt,
            OrderId = key.OrderId
        };
    }

    public async Task<SteamKeyDetailResponse> UpdateKeyAsync(
        Guid productId,
        Guid keyId,
        string newKeyValue,
        CancellationToken cancellationToken = default)
    {
        var key = await _dbContext.SteamKeys
            .FirstOrDefaultAsync(sk => sk.Id == keyId && sk.ProductId == productId, cancellationToken);

        if (key == null)
        {
            throw new KeyNotFoundException("Steam key not found.");
        }

        if (key.Status == (int)SteamKeyStatus.Sold)
        {
            throw new InvalidOperationException("Sold keys cannot be modified.");
        }

        var normalized = newKeyValue.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 5 || normalized.Length > 50)
        {
            throw new ArgumentException("Invalid key format. Key must be between 5 and 50 characters.");
        }

        var duplicateExists = await _dbContext.SteamKeys
            .AnyAsync(sk =>
                sk.ProductId == productId &&
                sk.Id != keyId &&
                sk.KeyValue.ToUpper() == normalized,
            cancellationToken);

        if (duplicateExists)
        {
            throw new InvalidOperationException("A key with the same value already exists for this product.");
        }

        key.KeyValue = normalized;
        key.Status = (int)SteamKeyStatus.Available;
        key.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncProductStockAsync(productId, cancellationToken);

        _logger?.LogInformation(
            "[SteamKeys.Update] KeyId={KeyId} ProductId={ProductId}",
            keyId, productId);

        return new SteamKeyDetailResponse
        {
            Id = key.Id,
            ProductId = key.ProductId,
            KeyValue = key.KeyValue,
            Status = key.Status,
            StatusName = MapStatusToName(key.Status),
            CreatedAt = key.CreatedAt,
            UsedAt = key.UsedAt,
            OrderId = key.OrderId
        };
    }

    public async Task DisableKeyAsync(
        Guid productId,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var key = await _dbContext.SteamKeys
            .FirstOrDefaultAsync(sk => sk.Id == keyId && sk.ProductId == productId, cancellationToken);

        if (key == null)
        {
            throw new KeyNotFoundException("Steam key not found.");
        }

        if (key.Status == (int)SteamKeyStatus.Sold)
        {
            throw new InvalidOperationException("Sold keys cannot be disabled.");
        }

        key.Status = (int)SteamKeyStatus.Disabled;
        key.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncProductStockAsync(productId, cancellationToken);

        _logger?.LogInformation(
            "[SteamKeys.Disable] KeyId={KeyId} ProductId={ProductId}",
            keyId, productId);
    }

    public async Task EnableKeyAsync(
        Guid productId,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var key = await _dbContext.SteamKeys
            .FirstOrDefaultAsync(sk => sk.Id == keyId && sk.ProductId == productId, cancellationToken);

        if (key == null)
        {
            throw new KeyNotFoundException("Steam key not found.");
        }

        if (key.Status == (int)SteamKeyStatus.Sold)
        {
            throw new InvalidOperationException("Sold keys cannot be enabled.");
        }

        key.Status = (int)SteamKeyStatus.Available;
        key.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncProductStockAsync(productId, cancellationToken);

        _logger?.LogInformation(
            "[SteamKeys.Enable] KeyId={KeyId} ProductId={ProductId}",
            keyId, productId);
    }

    public async Task DeleteKeyAsync(
        Guid productId,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var key = await _dbContext.SteamKeys
            .FirstOrDefaultAsync(sk => sk.Id == keyId && sk.ProductId == productId, cancellationToken);

        if (key == null)
        {
            throw new KeyNotFoundException("Steam key not found.");
        }

        if (key.Status == (int)SteamKeyStatus.Sold)
        {
            throw new InvalidOperationException("Sold keys cannot be deleted.");
        }

        _dbContext.SteamKeys.Remove(key);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncProductStockAsync(productId, cancellationToken);

        _logger?.LogInformation(
            "[SteamKeys.Delete] KeyId={KeyId} ProductId={ProductId}",
            keyId, productId);
    }

    public async Task<SteamKeySummaryResponse> GetSummaryAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var keys = _dbContext.SteamKeys.AsNoTracking().Where(sk => sk.ProductId == productId);

        var available = await keys.CountAsync(sk => sk.Status == (int)SteamKeyStatus.Available, cancellationToken);
        var sold = await keys.CountAsync(sk => sk.Status == (int)SteamKeyStatus.Sold, cancellationToken);
        var disabled = await keys.CountAsync(sk => sk.Status == (int)SteamKeyStatus.Disabled, cancellationToken);
        var total = available + sold + disabled;

        return new SteamKeySummaryResponse
        {
            Available = available,
            Sold = sold,
            Disabled = disabled,
            Total = total
        };
    }

    private static string MapStatusToName(int status) => status switch
    {
        (int)SteamKeyStatus.Available => "Available",
        (int)SteamKeyStatus.Disabled => "Disabled",
        (int)SteamKeyStatus.Sold => "Sold",
        _ => "Disabled"
    };

    private static int? MapFrontendStatus(string status) => status.ToLowerInvariant() switch
    {
        "available" => (int)SteamKeyStatus.Available,
        "disabled" => (int)SteamKeyStatus.Disabled,
        "sold" => (int)SteamKeyStatus.Sold,
        _ => null
    };

    private static string MaskKey(string keyValue)
    {
        if (string.IsNullOrEmpty(keyValue) || keyValue.Length <= 5)
        {
            return "****";
        }

        var visibleChars = Math.Min(5, keyValue.Length);
        var masked = new string('*', Math.Max(0, keyValue.Length - visibleChars));
        return keyValue[..visibleChars] + masked;
    }
}
