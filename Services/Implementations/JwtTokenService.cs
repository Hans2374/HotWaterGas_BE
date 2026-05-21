using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtTokenOptions _jwtOptions;

    public JwtTokenService(IOptions<JwtTokenOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public string GenerateToken(Users user, string roleName)
    {
        if (string.IsNullOrEmpty(_jwtOptions.Key))
        {
            throw new InvalidOperationException("Jwt:Key is not configured.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            // ClaimTypes.NameIdentifier = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
            // This is the standard claim type that JWT bearer middleware resolves to by default.
            // Do NOT use a plain string literal "UserId" here — FindFirstValue() does exact
            // string comparison and would fail to match, causing 401 on every authenticated request.
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, roleName),
            new("displayName", user.DisplayName)
        };

        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTime GetAccessTokenExpiry()
    {
        return DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpiryMinutes);
    }
}
