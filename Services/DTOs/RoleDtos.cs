namespace Services.DTOs;

// ─── Role List Item (for table display) ───────────────────────────────────

public class RoleListItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UsersCount { get; set; }
}

// ─── Role Detail (for edit/view) ─────────────────────────────────────────

public class RoleDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UsersCount { get; set; }
}

// ─── Create Role Request ─────────────────────────────────────────────────

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
}

// ─── Update Role Request ─────────────────────────────────────────────────

public class UpdateRoleRequest
{
    public string Name { get; set; } = string.Empty;
}

// ─── Get Roles Request ─────────────────────────────────────────────────

public class GetAdminRolesRequest : PagedRequest
{
    public string? Search { get; set; }
}
