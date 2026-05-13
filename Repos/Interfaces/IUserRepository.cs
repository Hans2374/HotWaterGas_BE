using Microsoft.EntityFrameworkCore.Storage;
using Repos.Models;

namespace Repos.Interfaces;

public interface IUserRepository
{
    Task<Users?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<Users?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<Roles?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default);
    Task AddRoleAsync(Roles role, CancellationToken cancellationToken = default);

    Task<EmailVerifications?> GetEmailVerificationByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PasswordResets?> GetLatestPasswordResetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<PasswordResets?> GetPasswordResetByResetTokenHashAsync(string resetTokenHash, CancellationToken cancellationToken = default);

    Task AddUserAsync(Users user, CancellationToken cancellationToken = default);
    Task AddEmailVerificationAsync(EmailVerifications emailVerification, CancellationToken cancellationToken = default);
    Task AddPasswordResetAsync(PasswordResets passwordReset, CancellationToken cancellationToken = default);

    void UpdateUser(Users user);
    void UpdateEmailVerification(EmailVerifications emailVerification);
    void UpdatePasswordReset(PasswordResets passwordReset);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
