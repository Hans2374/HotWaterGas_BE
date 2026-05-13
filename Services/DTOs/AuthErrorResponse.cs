namespace Services.DTOs;

/// <summary>
/// Standardized authentication failure response returned for all JWT validation errors.
/// Used by JwtBearerEvents to ensure consistent JSON shape across all auth failure paths.
/// </summary>
public class AuthErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
