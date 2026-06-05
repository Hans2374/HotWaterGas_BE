namespace Services.DTOs;

// ─── Developer List Item (for table display) ───────────────────────────────────

public class DeveloperListItemResponse
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

// ─── Developer Detail (for edit/view) ─────────────────────────────────────────

public class DeveloperDetailResponse
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

// ─── Create Developer Request ─────────────────────────────────────────────────

public class CreateDeveloperRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
}

// ─── Update Developer Request ─────────────────────────────────────────────────

public class UpdateDeveloperRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
}

// ─── Get Admin Developers Request ─────────────────────────────────────────────

public class GetAdminDevelopersRequest : PagedRequest
{
    public string? Search { get; set; }
}
