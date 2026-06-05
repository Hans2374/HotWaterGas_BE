namespace Services.DTOs;

// ─── Publisher List Item (for table display) ───────────────────────────────────

public class PublisherListItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
    public int AttachedProductsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ─── Publisher Detail (for edit/view) ─────────────────────────────────────────

public class PublisherDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
    public int AttachedProductsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ─── Create Publisher Request ─────────────────────────────────────────────────

public class CreatePublisherRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
}

// ─── Update Publisher Request ─────────────────────────────────────────────────

public class UpdatePublisherRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
}

// ─── Get Admin Publishers Request ─────────────────────────────────────────────

public class GetAdminPublishersRequest : PagedRequest
{
    public string? Search { get; set; }
}
