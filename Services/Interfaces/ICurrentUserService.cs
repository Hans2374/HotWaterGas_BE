using System.Security.Claims;

namespace Services.Interfaces;

/// <summary>
/// Provides typed access to the authenticated user identity extracted from the
/// validated JWT claims principal for the current HTTP request.
///
/// All services and controllers must use this abstraction rather than directly
/// reading claims from HttpContext.User. This ensures consistent claim-type
/// resolution across the entire codebase and centralises fallback logic in
/// one place.
///
/// The service is scoped to the current HTTP request so a single request
/// always resolves to the same identity.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// The authenticated user's ID, or null if no valid user ID could be parsed
    /// from any recognised claim on the current principal.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// The authenticated user's email address extracted from the email claim, or null.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// The authenticated user's role name (e.g. "Customer", "Admin"), or null.
    /// </summary>
    string? Role { get; }

    /// <summary>
    /// Short-hand for UserId.HasValue.
    /// </summary>
    bool IsAuthenticated { get; }
}
