using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Repos.Interfaces;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AuthService : IAuthService
{
    private const int VerificationCodeLifetimeMinutes = 15;
    private const int ResetCodeLifetimeMinutes = 15;
    private const int ResetTokenLifetimeMinutes = 15;

    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthTokenOptions _authOptions;

    public AuthService(
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        IEmailService emailService,
        ILogger<AuthService> logger,
        IOptions<AuthTokenOptions> authOptions)
    {
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _emailService = emailService;
        _logger = logger;
        _authOptions = authOptions.Value;
    }

    public async Task<MessageResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        if (await _userRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            throw new ApiException(409, "Email is already registered.");
        }

        var customerRole = await _userRepository.GetRoleByNameAsync("Customer", cancellationToken);
        if (customerRole is null)
        {
            customerRole = await _userRepository.GetRoleByNameAsync("User", cancellationToken);
        }

        if (customerRole is null)
        {
            customerRole = new Roles
            {
                Id = Guid.NewGuid(),
                Name = "Customer"
            };

            await _userRepository.AddRoleAsync(customerRole, cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("[Auth.Register] Missing role configuration. Auto-created role {RoleName} with Id={RoleId}", customerRole.Name, customerRole.Id);
        }

        var verificationCode = GenerateSixDigitCode();

        // Auto-generate DisplayName from email prefix if not provided.
        // Apply same length constraints here (server-side, replacing DTO-level validation).
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? normalizedEmail.Split('@')[0]
            : request.DisplayName.Trim();

        if (displayName.Length > 100)
        {
            displayName = displayName[..100];
        }
        // Ensure minimum length (pad with trailing digit if email prefix is too short,
        // e.g. "a@b.com" → "a1" to satisfy the 2-char minimum).
        if (displayName.Length < 2)
        {
            displayName = displayName + "1";
        }

        var user = new Users
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow,
            RoleId = customerRole.Id,
            IsEmailVerified = false,
            DisplayName = displayName
        };

        var verification = new EmailVerifications
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = HashValue(verificationCode),
            ExpiresAt = DateTime.UtcNow.AddMinutes(VerificationCodeLifetimeMinutes),
            VerifiedAt = null,
            AttemptCount = 0,
            LastSentAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        await using var registrationTransaction = await _userRepository.BeginTransactionAsync(cancellationToken);

        await _userRepository.AddUserAsync(user, cancellationToken);
        await _userRepository.AddEmailVerificationAsync(verification, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);
        await registrationTransaction.CommitAsync(cancellationToken);

        _logger.LogInformation("[Auth.Register] Created UserId={UserId} Email={Email}", user.Id, normalizedEmail);

        try
        {
            _logger.LogInformation("[MailerSend] Sending verification email to {Email}", normalizedEmail);
            await _emailService.SendVerificationEmailAsync(normalizedEmail, verificationCode, cancellationToken);
            _logger.LogInformation("[Auth.Register] Verification email sent UserId={UserId} Email={Email}", user.Id, normalizedEmail);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[Auth.Register] Verification email failed Email={Email}", normalizedEmail);
        }

        return new MessageResponse
        {
            Message = "Registration successful. Please check your email for the verification code."
        };
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _userRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("[Auth.Login] User not found email={Email}", normalizedEmail);
            throw new ApiException(400, "Email hoặc mật khẩu không đúng.");
        }

        // Google OAuth users have no local password - reject safely
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            _logger.LogWarning("[Auth.Login] Google OAuth account login attempt email={Email}", normalizedEmail);
            throw new ApiException(400, "Tài khoản này được đăng ký bằng Google. Hãy đăng nhập bằng Google");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("[Auth.Login] Invalid password for email={Email}", normalizedEmail);
            throw new ApiException(400, "Email hoặc mật khẩu không đúng.");
        }

        var roleName = user.Role?.Name ?? "Customer";
        var token = _jwtTokenService.GenerateToken(user, roleName);

        _logger.LogInformation("[Auth.Login] Success UserId={UserId} Email={Email}", user.Id, normalizedEmail);

        return new LoginResponse
        {
            AccessToken = token,
            Role = roleName,
            User = new AuthUserResponse
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = roleName
            }
        };
    }

    public async Task<GoogleAuthResponse> GoogleAuthAsync(
        string googleId,
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);

        // Try to find existing user by Google ID first
        var user = await _userRepository.GetUserByGoogleIdAsync(googleId, cancellationToken);

        bool isNewUser = false;

        // If not found by Google ID, try to find by email and link the account
        if (user is null)
        {
            user = await _userRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

            if (user is not null)
            {
                // Link existing account to Google
                user.GoogleId = googleId;
                _userRepository.UpdateUser(user);
                await _userRepository.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "[Auth.Google] Linked existing account GoogleId={GoogleId} Email={Email} UserId={UserId}",
                    googleId, normalizedEmail, user.Id);
            }
            else
            {
                // Create new user
                var customerRole = await _userRepository.GetRoleByNameAsync("Customer", cancellationToken);
                if (customerRole is null)
                {
                    customerRole = await _userRepository.GetRoleByNameAsync("User", cancellationToken);
                }

                if (customerRole is null)
                {
                    customerRole = new Roles
                    {
                        Id = Guid.NewGuid(),
                        Name = "Customer"
                    };

                    await _userRepository.AddRoleAsync(customerRole, cancellationToken);
                    await _userRepository.SaveChangesAsync(cancellationToken);
                    _logger.LogWarning(
                        "[Auth.Google] Missing role configuration. Auto-created role {RoleName} with Id={RoleId}",
                        customerRole.Name, customerRole.Id);
                }

                user = new Users
                {
                    Id = Guid.NewGuid(),
                    Email = normalizedEmail,
                    PasswordHash = null, // Google users don't need password
                    GoogleId = googleId,
                    CreatedAt = DateTime.UtcNow,
                    RoleId = customerRole.Id,
                    IsEmailVerified = true, // Google already verified the email
                    DisplayName = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : normalizedEmail.Split('@')[0]
                };

                await _userRepository.AddUserAsync(user, cancellationToken);
                await _userRepository.SaveChangesAsync(cancellationToken);

                isNewUser = true;

                _logger.LogInformation(
                    "[Auth.Google] Created new user GoogleId={GoogleId} Email={Email} UserId={UserId}",
                    googleId, normalizedEmail, user.Id);
            }
        }
        else
        {
            _logger.LogInformation(
                "[Auth.Google] Logged in existing user GoogleId={GoogleId} Email={Email} UserId={UserId}",
                googleId, normalizedEmail, user.Id);
        }

        var roleName = user.Role?.Name ?? "Customer";
        var token = _jwtTokenService.GenerateToken(user, roleName);

        return new GoogleAuthResponse
        {
            AccessToken = token,
            Role = roleName,
            User = new AuthUserResponse
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = roleName
            },
            AccessTokenExpiresAt = _jwtTokenService.GetAccessTokenExpiry(),
            IsNewUser = isNewUser
        };
    }

    public string GenerateAccessTokenForRefresh(Repos.Models.Users user)
    {
        var roleName = user.Role?.Name ?? "Customer";
        return _jwtTokenService.GenerateToken(user, roleName);
    }

    public async Task<MessageResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _userRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("[Auth.VerifyEmail] User not found email={Email}", normalizedEmail);
            throw new ApiException(400, "Email hoặc mã xác minh không đúng.");
        }

        var verification = await _userRepository.GetEmailVerificationByUserIdAsync(user.Id, cancellationToken);
        if (verification is null)
        {
            _logger.LogWarning("[Auth.VerifyEmail] No verification record UserId={UserId}", user.Id);
            throw new ApiException(400, "Mã xác minh không đúng.");
        }

        if (verification.VerifiedAt.HasValue || verification.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("[Auth.VerifyEmail] Code expired or already verified UserId={UserId}", user.Id);
            throw new ApiException(400, "Mã xác minh đã hết hạn hoặc đã được xác minh.");
        }

        var codeHash = HashValue(request.Code.Trim());
        if (!string.Equals(verification.CodeHash, codeHash, StringComparison.Ordinal))
        {
            verification.AttemptCount += 1;
            _userRepository.UpdateEmailVerification(verification);
            await _userRepository.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("[Auth.VerifyEmail] Invalid code UserId={UserId} Attempts={Attempts}", user.Id, verification.AttemptCount);
            throw new ApiException(400, "Mã xác minh không đúng hoặc đã hết hạn.");
        }

        user.IsEmailVerified = true;
        verification.VerifiedAt = DateTime.UtcNow;
        verification.CodeHash = string.Empty;

        _userRepository.UpdateUser(user);
        _userRepository.UpdateEmailVerification(verification);
        await _userRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[Auth.VerifyEmail] Verified UserId={UserId} Email={Email}", user.Id, normalizedEmail);
        return new MessageResponse { Message = "Email xác minh thành công." };
    }

    public async Task<MessageResponse> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _userRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("[Auth.ResendVerification] Email not found {Email}", normalizedEmail);
            return new MessageResponse { Message = "Nếu email tồn tại, mã xác minh đã được gửi." };
        }

        if (user.IsEmailVerified)
        {
            _logger.LogInformation("[Auth.ResendVerification] Email already verified UserId={UserId} Email={Email}", user.Id, normalizedEmail);
            return new MessageResponse { Message = "Email đã được xác minh." };
        }

        var code = GenerateSixDigitCode();
        var verification = await _userRepository.GetEmailVerificationByUserIdAsync(user.Id, cancellationToken);

        await using var resendTransaction = await _userRepository.BeginTransactionAsync(cancellationToken);

        try
        {
            if (verification is null)
            {
                verification = new EmailVerifications
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    CodeHash = HashValue(code),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(VerificationCodeLifetimeMinutes),
                    VerifiedAt = null,
                    AttemptCount = 0,
                    LastSentAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                await _userRepository.AddEmailVerificationAsync(verification, cancellationToken);
            }
            else
            {
                verification.CodeHash = HashValue(code);
                verification.ExpiresAt = DateTime.UtcNow.AddMinutes(VerificationCodeLifetimeMinutes);
                verification.VerifiedAt = null;
                verification.AttemptCount = 0;
                verification.LastSentAt = DateTime.UtcNow;
                _userRepository.UpdateEmailVerification(verification);
            }

            await _userRepository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[MailerSend] Sending verification email to {Email}", normalizedEmail);

            await _emailService.SendVerificationEmailAsync(normalizedEmail, code, cancellationToken);

            await resendTransaction.CommitAsync(cancellationToken);
            _logger.LogInformation("[Auth.ResendVerification] Verification email sent UserId={UserId} Email={Email}", user.Id, normalizedEmail);
        }
        catch (Exception exception)
        {
            await resendTransaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "[Auth.ResendVerification] Verification email failed UserId={UserId} Email={Email}", user.Id, normalizedEmail);
            throw new ApiException(502, "Unable to send verification email. Please try again later.");
        }

        return new MessageResponse { Message = "Mã xác minh đã được gửi lại." };
    }

    public async Task<MessageResponse> ForgotPasswordRequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _userRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

        if (user is not null)
        {
            var code = GenerateSixDigitCode();
            var reset = new PasswordResets
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Email = normalizedEmail,
                CodeHash = HashValue(code),
                ExpiresAt = DateTime.UtcNow.AddMinutes(ResetCodeLifetimeMinutes),
                VerifiedAt = null,
                ConsumedAt = null,
                AttemptCount = 0,
                LastSentAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                ResetTokenHash = string.Empty,
                ResetTokenExpiresAt = null
            };

            await using var resetTransaction = await _userRepository.BeginTransactionAsync(cancellationToken);

            try
            {
                await _userRepository.AddPasswordResetAsync(reset, cancellationToken);
                await _userRepository.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("[MailerSend] Sending password reset email to {Email}", normalizedEmail);
                await _emailService.SendPasswordResetEmailAsync(normalizedEmail, code, cancellationToken);

                await resetTransaction.CommitAsync(cancellationToken);
                _logger.LogInformation("[Auth.ForgotPassword.Request] Password reset email sent UserId={UserId} Email={Email}", user.Id, normalizedEmail);
            }
            catch (Exception exception)
            {
                await resetTransaction.RollbackAsync(cancellationToken);
                _logger.LogError(exception, "[Auth.ForgotPassword.Request] Password reset email failed UserId={UserId} Email={Email}", user.Id, normalizedEmail);
                throw new ApiException(502, "Unable to send password reset email. Please try again later.");
            }
        }

        // Do not leak whether the email exists.
        return new MessageResponse { Message = "Nếu email tồn tại, mã đặt lại đã được gửi." };
    }

    public async Task<ForgotPasswordVerifyResponse> ForgotPasswordVerifyAsync(ForgotPasswordVerifyRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var reset = await _userRepository.GetLatestPasswordResetByEmailAsync(normalizedEmail, cancellationToken);

        if (reset is null || reset.ConsumedAt.HasValue || reset.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("[Auth.ForgotPassword.Verify] Code not found or expired Email={Email}", normalizedEmail);
            throw new ApiException(400, "Mã đặt lại không đúng hoặc đã hết hạn.");
        }

        var codeHash = HashValue(request.Code.Trim());
        if (!string.Equals(reset.CodeHash, codeHash, StringComparison.Ordinal))
        {
            reset.AttemptCount += 1;
            _userRepository.UpdatePasswordReset(reset);
            await _userRepository.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("[Auth.ForgotPassword.Verify] Invalid code Email={Email} Attempts={Attempts}", normalizedEmail, reset.AttemptCount);
            throw new ApiException(400, "Mã đặt lại không đúng hoặc đã hết hạn.");
        }

        var plainResetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        reset.VerifiedAt = DateTime.UtcNow;
        reset.ResetTokenHash = HashValue(plainResetToken);
        reset.ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(ResetTokenLifetimeMinutes);

        _userRepository.UpdatePasswordReset(reset);
        await _userRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[Auth.ForgotPassword.Verify] Code verified Email={Email}", normalizedEmail);

        return new ForgotPasswordVerifyResponse
        {
            ResetToken = plainResetToken
        };
    }

    public async Task<MessageResponse> ForgotPasswordResetAsync(ForgotPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var resetTokenHash = HashValue(request.ResetToken.Trim());

        var reset = await _userRepository.GetPasswordResetByResetTokenHashAsync(resetTokenHash, cancellationToken);
        if (reset is null || reset.ConsumedAt.HasValue || reset.ResetTokenExpiresAt is null || reset.ResetTokenExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("[Auth.ForgotPassword.Reset] Invalid or expired token Email={Email}", normalizedEmail);
            throw new ApiException(400, "Mã đặt lại không đúng hoặc đã hết hạn.");
        }

        if (!string.Equals(reset.Email, normalizedEmail, StringComparison.Ordinal))
        {
            _logger.LogWarning("[Auth.ForgotPassword.Reset] Email mismatch TokenEmail={TokenEmail} RequestEmail={RequestEmail}", reset.Email, normalizedEmail);
            throw new ApiException(400, "Mã đặt lại không đúng hoặc đã hết hạn.");
        }

        Users? user = null;
        if (reset.UserId.HasValue)
        {
            user = await _userRepository.GetUserByIdAsync(reset.UserId.Value, cancellationToken);
        }

        user ??= await _userRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("[Auth.ForgotPassword.Reset] User not found Email={Email}", normalizedEmail);
            throw new ApiException(400, "Yêu cầu đặt lại không hợp lệ.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        reset.ConsumedAt = DateTime.UtcNow;
        reset.ResetTokenHash = string.Empty;
        reset.ResetTokenExpiresAt = null;
        reset.CodeHash = string.Empty;

        _userRepository.UpdateUser(user);
        _userRepository.UpdatePasswordReset(reset);
        await _userRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[Auth.ForgotPassword.Reset] Password reset UserId={UserId} Email={Email}", user.Id, normalizedEmail);

        return new MessageResponse { Message = "Mật khẩu đã được đặt lại thành công." };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string GenerateSixDigitCode()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }

    private static string HashValue(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public async Task<UserProfileResponse> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ApiException(404, "Không tìm thấy người dùng.");
        }

        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role?.Name ?? "Customer",
            IsEmailVerified = user.IsEmailVerified
        };
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ApiException(404, "Không tìm thấy người dùng.");
        }

        user.DisplayName = request.DisplayName.Trim();

        _userRepository.UpdateUser(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[Auth.UpdateProfile] Updated DisplayName UserId={UserId}", userId);

        // Return a new access token so the frontend can immediately sync the displayName
        // in AuthContext without requiring the user to re-authenticate.
        var roleName = user.Role?.Name ?? "Customer";
        var newAccessToken = _jwtTokenService.GenerateToken(user, roleName);

        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = roleName,
            IsEmailVerified = user.IsEmailVerified,
            AccessToken = newAccessToken
        };
    }

    public async Task<MessageResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new ApiException(400, "Mật khẩu mới và xác nhận mật khẩu không khớp.");
        }

        var user = await _userRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ApiException(404, "Không tìm thấy người dùng.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new ApiException(400, "Mật khẩu hiện tại không đúng.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        _userRepository.UpdateUser(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[Auth.ChangePassword] Password changed UserId={UserId}", userId);

        return new MessageResponse { Message = "Mật khẩu đã được thay đổi thành công." };
    }

    public int GetRefreshTokenExpiryDays()
    {
        return _authOptions.RefreshTokenExpiryDays;
    }
}
