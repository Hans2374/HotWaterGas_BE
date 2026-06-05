using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminPublisherService
{
    Task<PagedResponse<PublisherListItemResponse>> GetPublishersAsync(
        GetAdminPublishersRequest request,
        CancellationToken cancellationToken = default);

    Task<PublisherDetailResponse?> GetPublisherByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PublisherDetailResponse> CreatePublisherAsync(
        CreatePublisherRequest request,
        CancellationToken cancellationToken = default);

    Task<PublisherDetailResponse> UpdatePublisherAsync(
        Guid id,
        UpdatePublisherRequest request,
        CancellationToken cancellationToken = default);

    Task DeletePublisherAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
