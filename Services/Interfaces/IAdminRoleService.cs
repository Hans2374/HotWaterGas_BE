using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminRoleService
{
    Task<PagedResponse<RoleListItemResponse>> GetRolesAsync(
        GetAdminRolesRequest request,
        CancellationToken cancellationToken = default);

    Task<RoleDetailResponse?> GetRoleByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<RoleDetailResponse> CreateRoleAsync(
        CreateRoleRequest request,
        CancellationToken cancellationToken = default);

    Task<RoleDetailResponse> UpdateRoleAsync(
        Guid id,
        UpdateRoleRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
