namespace Services.DTOs;

/// <summary>
/// Request parameters for GetAdminCategories endpoint.
/// </summary>
public class GetAdminCategoriesRequest : PagedRequest
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
}
