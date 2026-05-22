using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Repos.Interfaces;
using Repos.Models;

namespace Repos.Implementations;

public class UserRepository : IUserRepository
{
    private readonly HotWaterGasDBContext _dbContext;

    public UserRepository(HotWaterGasDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Users?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
    }

    public Task<Users?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public Task<Users?> GetUserByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.GoogleId == googleId, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
    }

    public Task<Roles?> GetRoleByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var normalizedRoleName = roleName.Trim().ToLowerInvariant();
        return _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Name != null && r.Name.ToLower() == normalizedRoleName, cancellationToken);
    }

    public Task AddRoleAsync(Roles role, CancellationToken cancellationToken = default)
    {
        return _dbContext.Roles.AddAsync(role, cancellationToken).AsTask();
    }

    public Task<EmailVerifications?> GetEmailVerificationByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.EmailVerifications
            .FirstOrDefaultAsync(v => v.UserId == userId, cancellationToken);
    }

    public Task<PasswordResets?> GetLatestPasswordResetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResets
            .Where(r => r.Email == normalizedEmail)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<PasswordResets?> GetPasswordResetByResetTokenHashAsync(string resetTokenHash, CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResets
            .FirstOrDefaultAsync(r => r.ResetTokenHash == resetTokenHash, cancellationToken);
    }

    public Task AddUserAsync(Users user, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AddAsync(user, cancellationToken).AsTask();
    }

    public Task AddEmailVerificationAsync(EmailVerifications emailVerification, CancellationToken cancellationToken = default)
    {
        return _dbContext.EmailVerifications.AddAsync(emailVerification, cancellationToken).AsTask();
    }

    public Task AddPasswordResetAsync(PasswordResets passwordReset, CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResets.AddAsync(passwordReset, cancellationToken).AsTask();
    }

    public void UpdateUser(Users user)
    {
        _dbContext.Users.Update(user);
    }

    public void UpdateEmailVerification(EmailVerifications emailVerification)
    {
        _dbContext.EmailVerifications.Update(emailVerification);
    }

    public void UpdatePasswordReset(PasswordResets passwordReset)
    {
        _dbContext.PasswordResets.Update(passwordReset);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
