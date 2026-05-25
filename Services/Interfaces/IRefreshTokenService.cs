using Microsoft.AspNetCore.Http;
using Repos.Models;

namespace Services.Interfaces;

public interface IRefreshTokenService
{
    Task<(string Token, string TokenHash, DateTime ExpiresAtUtc)> GenerateRefreshTokenAsync(
        Guid userId,
        string? createdByIp,
        string? userAgent,
        string? deviceInfo,
        Guid? parentTokenId = null,
        Guid? tokenFamilyId = null,
        CancellationToken cancellationToken = default);

    Task<(string Token, string TokenHash, DateTime ExpiresAtUtc)> GenerateRefreshTokenAsync(
        Guid userId,
        string? createdByIp,
        string? userAgent,
        string? deviceInfo,
        int expiryDays,
        Guid? parentTokenId = null,
        Guid? tokenFamilyId = null,
        CancellationToken cancellationToken = default);

    Task<string> HashTokenAsync(string plainToken);

    Task<(bool IsValid, RefreshTokens? Token, string? ErrorCode)> ValidateTokenAsync(
        string plainToken,
        CancellationToken cancellationToken = default);

    Task<RefreshTokens?> GetTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task RevokeTokenAsync(RefreshTokens token, string? revokedByIp, CancellationToken cancellationToken = default);

    Task RevokeTokenFamilyAsync(Guid tokenFamilyId, string? revokedByIp, CancellationToken cancellationToken = default);

    Task<(RefreshTokens? Token, string? PlainToken)> RotateTokenAsync(
        RefreshTokens currentToken,
        string? revokedByIp,
        string? userAgent,
        string? deviceInfo,
        CancellationToken cancellationToken = default);

    Task UpdateLastUsedAsync(Guid tokenId, CancellationToken cancellationToken = default);

    void SetRefreshCookie(HttpContext httpContext, string token, DateTime expiresAtUtc);

    void SetSessionCookie(HttpContext httpContext, string token);

    void ClearRefreshCookie(HttpContext httpContext);

    string? GetRefreshTokenFromCookie(HttpContext httpContext);

    Task<IEnumerable<RefreshTokens>> GetUserActiveSessionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsTokenReusedAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);
}
