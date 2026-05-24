using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminUserService
{
    Task<PagedAdminUserListResponse> GetUsersAsync(AdminUserQueryRequest query, CancellationToken cancellationToken = default);
    Task<AdminUserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ToggleSuspensionAsync(Guid userId, Guid adminUserId, CancellationToken cancellationToken = default);
}
