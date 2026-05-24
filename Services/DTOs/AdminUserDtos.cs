namespace Services.DTOs;

public class AdminUserTableItemDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public bool IsSuspended { get; set; }
    public int OrdersCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminUserQueryRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? Role { get; set; }
    public bool? IsSuspended { get; set; }
}

public class PagedAdminUserListResponse
{
    public List<AdminUserTableItemDto> Data { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}
