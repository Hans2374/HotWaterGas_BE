using Services.DTOs;

namespace Services.Interfaces;

public interface IAuthService
{
    Task<MessageResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<MessageResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task<MessageResponse> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default);
    Task<MessageResponse> ForgotPasswordRequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<ForgotPasswordVerifyResponse> ForgotPasswordVerifyAsync(ForgotPasswordVerifyRequest request, CancellationToken cancellationToken = default);
    Task<MessageResponse> ForgotPasswordResetAsync(ForgotPasswordResetRequest request, CancellationToken cancellationToken = default);
    string GenerateAccessTokenForRefresh(Repos.Models.Users user);
    Task<UserProfileResponse> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<MessageResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
