namespace Services.DTOs;

// ─── Category List Item (for table display) ───────────────────────────────────

public class CategoryListItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public int AttachedProductsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ─── Category Detail (for edit/view) ─────────────────────────────────────────

public class CategoryDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public int AttachedProductsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ─── Create Category Request ─────────────────────────────────────────────────

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ImageUrl { get; set; }
}

// ─── Update Category Request ─────────────────────────────────────────────────

public class UpdateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }
}

// ─── Homepage Category Item ──────────────────────────────────────────────────────

public class CategoryHomepageResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

// ─── Delete Blocked Response ──────────────────────────────────────────────────

public class CategoryDeleteBlockedResponse
{
    public string Message { get; set; } = string.Empty;
    public int AttachedProductsCount { get; set; }
}
