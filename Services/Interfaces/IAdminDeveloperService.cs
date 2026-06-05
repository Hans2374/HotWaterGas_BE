using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminDeveloperService
{
    Task<PagedResponse<DeveloperListItemResponse>> GetDevelopersAsync(
        GetAdminDevelopersRequest request,
        CancellationToken cancellationToken = default);

    Task<DeveloperDetailResponse?> GetDeveloperByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<DeveloperDetailResponse> CreateDeveloperAsync(
        CreateDeveloperRequest request,
        CancellationToken cancellationToken = default);

    Task<DeveloperDetailResponse> UpdateDeveloperAsync(
        Guid id,
        UpdateDeveloperRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteDeveloperAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
