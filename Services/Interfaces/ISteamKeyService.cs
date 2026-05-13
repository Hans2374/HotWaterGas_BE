using Services.DTOs;

namespace Services.Interfaces;

public interface ISteamKeyService
{
    Task<SteamKeyBulkUploadResponse> BulkUploadAsync(Guid productId, List<string> keyValues, CancellationToken cancellationToken = default);
    Task<SteamKeyListResponse> GetKeysAsync(Guid productId, int page, int pageSize, string? status, CancellationToken cancellationToken = default);
    Task<SteamKeyDetailResponse?> GetKeyByIdAsync(Guid productId, Guid keyId, CancellationToken cancellationToken = default);
    Task<SteamKeyDetailResponse> UpdateKeyAsync(Guid productId, Guid keyId, string newKeyValue, CancellationToken cancellationToken = default);
    Task DisableKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken = default);
    Task EnableKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken = default);
    Task DeleteKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken = default);
    Task<SteamKeySummaryResponse> GetSummaryAsync(Guid productId, CancellationToken cancellationToken = default);
}
