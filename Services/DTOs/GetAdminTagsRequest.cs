namespace Services.DTOs;

/// <summary>
/// Request parameters for GetAdminTags endpoint.
/// </summary>
public class GetAdminTagsRequest : PagedRequest
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
}
