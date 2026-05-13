using Repos.Models;

namespace Services.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Users user, string roleName);

    DateTime GetAccessTokenExpiry();
}
