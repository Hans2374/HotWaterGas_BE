using Services.DTOs;

namespace Services.Interfaces;

public interface IAdminTagService
{
    Task<PagedResponse<TagListItemResponse>> GetTagsAsync(
        GetAdminTagsRequest request,
        CancellationToken cancellationToken = default);

    Task<TagDetailResponse?> GetTagByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<TagDetailResponse> CreateTagAsync(
        CreateTagRequest request,
        CancellationToken cancellationToken = default);

    Task<TagDetailResponse> UpdateTagAsync(
        Guid id,
        UpdateTagRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteTagAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
