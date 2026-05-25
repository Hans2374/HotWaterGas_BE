namespace Services.DTOs;

public class MessageResponse
{
    public string Message { get; set; } = string.Empty;
}

public class AuthUserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public AuthUserResponse User { get; set; } = new();
}

public class LoginWithRefreshResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public AuthUserResponse User { get; set; } = new();
    public DateTime AccessTokenExpiresAt { get; set; }
    public bool RememberMe { get; set; } = false;
}

public class RefreshResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
}

public class AuthSessionInfoResponse
{
    public Guid SessionId { get; set; }
    public string DeviceInfo { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public bool IsCurrent { get; set; }
}

public class ForgotPasswordVerifyResponse
{
    public string ResetToken { get; set; } = string.Empty;
}

public class UserProfileResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsEmailVerified { get; set; }
    public string? AccessToken { get; set; }
}

public class GoogleAuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public AuthUserResponse User { get; set; } = new();
    public DateTime AccessTokenExpiresAt { get; set; }
    public bool IsNewUser { get; set; }
}
