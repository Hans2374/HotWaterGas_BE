using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Repos;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class RefreshTokenService : IRefreshTokenService
{
    private const int RefreshTokenEntropyBytes = 64;
    private readonly HotWaterGasDBContext _dbContext;
    private readonly AuthTokenOptions _authOptions;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        HotWaterGasDBContext dbContext,
        IOptions<AuthTokenOptions> authOptions,
        ILogger<RefreshTokenService> logger)
    {
        _dbContext = dbContext;
        _authOptions = authOptions.Value;
        _logger = logger;
    }

    public async Task<(string Token, string TokenHash, DateTime ExpiresAtUtc)> GenerateRefreshTokenAsync(
        Guid userId,
        string? createdByIp,
        string? userAgent,
        string? deviceInfo,
        Guid? parentTokenId = null,
        Guid? tokenFamilyId = null,
        CancellationToken cancellationToken = default)
    {
        var plainToken = GenerateSecureToken();
        var tokenHash = await HashTokenAsync(plainToken);
        var expiresAtUtc = DateTime.UtcNow.AddDays(_authOptions.RefreshTokenExpiryDays);

        var refreshToken = new RefreshTokens
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByIp = createdByIp,
            UserAgent = TruncateString(userAgent, 512),
            DeviceInfo = TruncateString(deviceInfo, 256),
            ParentTokenId = parentTokenId,
            TokenFamilyId = tokenFamilyId ?? Guid.NewGuid(),
            LastUsedAtUtc = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[RefreshToken.Issued] UserId={UserId} TokenFamilyId={TokenFamilyId} ExpiresAtUtc={ExpiresAtUtc}",
            userId, refreshToken.TokenFamilyId, expiresAtUtc);

        return (plainToken, tokenHash, expiresAtUtc);
    }

    public async Task<string> HashTokenAsync(string plainToken)
    {
        return await Task.Run(() =>
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        });
    }

    public async Task<(bool IsValid, RefreshTokens? Token, string? ErrorCode)> ValidateTokenAsync(
        string plainToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainToken))
        {
            return (false, null, "MISSING_TOKEN");
        }

        var tokenHash = await HashTokenAsync(plainToken);
        var token = await GetTokenByHashAsync(tokenHash, cancellationToken);

        if (token is null)
        {
            _logger.LogWarning("[RefreshToken.Validation] Token not found in database");
            return (false, null, "INVALID_TOKEN");
        }

        if (token.IsRevoked)
        {
            _logger.LogWarning(
                "[RefreshToken.Validation] Token is revoked UserId={UserId} TokenFamilyId={TokenFamilyId}",
                token.UserId, token.TokenFamilyId);
            return (false, null, "REVOKED_TOKEN");
        }

        if (token.IsExpired)
        {
            _logger.LogWarning(
                "[RefreshToken.Validation] Token is expired UserId={UserId} ExpiresAtUtc={ExpiresAtUtc}",
                token.UserId, token.ExpiresAtUtc);
            return (false, null, "EXPIRED_TOKEN");
        }

        return (true, token, null);
    }

    public async Task<RefreshTokens?> GetTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);
    }

    public async Task RevokeTokenAsync(RefreshTokens token, string? revokedByIp, CancellationToken cancellationToken = default)
    {
        if (!token.IsRevoked)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedByIp = revokedByIp;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[RefreshToken.Revoked] TokenId={TokenId} UserId={UserId} TokenFamilyId={TokenFamilyId}",
                token.Id, token.UserId, token.TokenFamilyId);
        }
    }

    public async Task RevokeTokenFamilyAsync(Guid tokenFamilyId, string? revokedByIp, CancellationToken cancellationToken = default)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(rt => rt.TokenFamilyId == tokenFamilyId && rt.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        var revokedCount = 0;
        foreach (var token in tokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedByIp = revokedByIp;
            revokedCount++;
        }

        if (revokedCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "[RefreshToken.FamilyRevoked] TokenFamilyId={TokenFamilyId} RevokedCount={RevokedCount}",
                tokenFamilyId, revokedCount);
        }
    }

    public async Task<(RefreshTokens? Token, string? PlainToken)> RotateTokenAsync(
        RefreshTokens currentToken,
        string? revokedByIp,
        string? userAgent,
        string? deviceInfo,
        CancellationToken cancellationToken = default)
    {
        await RevokeTokenAsync(currentToken, revokedByIp, cancellationToken);

        var (newPlainToken, newTokenHash, expiresAtUtc) = await GenerateRefreshTokenAsync(
            currentToken.UserId,
            revokedByIp,
            userAgent,
            deviceInfo,
            parentTokenId: currentToken.Id,
            tokenFamilyId: currentToken.TokenFamilyId,
            cancellationToken: cancellationToken);

        var newToken = await GetTokenByHashAsync(newTokenHash, cancellationToken);

        _logger.LogInformation(
            "[RefreshToken.Rotated] OldTokenId={OldTokenId} NewTokenId={NewTokenId} TokenFamilyId={TokenFamilyId}",
            currentToken.Id, newToken?.Id, currentToken.TokenFamilyId);

        return (newToken, newPlainToken);
    }

    public async Task UpdateLastUsedAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var token = await _dbContext.RefreshTokens.FindAsync([tokenId], cancellationToken);
        if (token is not null)
        {
            token.LastUsedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public void SetRefreshCookie(HttpContext httpContext, string token, DateTime expiresAtUtc)
    {
        var policy = _authOptions.GetSecurePolicy();
        var isSecure = policy switch
        {
            CookieSecurePolicy.Always => true,
            CookieSecurePolicy.SameAsRequest => httpContext.Request.IsHttps,
            CookieSecurePolicy.None => false,
            _ => true
        };

        var sameSite = _authOptions.GetSameSiteMode();

        // SameSite=None requires Secure=true per the SameSite spec.
        // Browsers (Chrome 114+) silently drop SameSite=None; Secure=false cookies.
        // Fall back to Lax on non-HTTPS origins so the cookie is stored and sent
        // on same-origin requests during local development.
        if (sameSite == SameSiteMode.None && !isSecure)
        {
            sameSite = SameSiteMode.Lax;
        }

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isSecure,
            SameSite = sameSite,
            Expires = expiresAtUtc,
            Path = "/",
            IsEssential = true
        };

        httpContext.Response.Cookies.Append(_authOptions.RefreshCookieName, token, cookieOptions);
    }

    public void ClearRefreshCookie(HttpContext httpContext)
    {
        var policy = _authOptions.GetSecurePolicy();
        var isSecure = policy switch
        {
            CookieSecurePolicy.Always => true,
            CookieSecurePolicy.SameAsRequest => httpContext.Request.IsHttps,
            CookieSecurePolicy.None => false,
            _ => true
        };

        var sameSite = _authOptions.GetSameSiteMode();

        if (sameSite == SameSiteMode.None && !isSecure)
        {
            sameSite = SameSiteMode.Lax;
        }

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isSecure,
            SameSite = sameSite,
            Expires = DateTime.UtcNow.AddDays(-1),
            Path = "/",
            IsEssential = true
        };

        httpContext.Response.Cookies.Delete(_authOptions.RefreshCookieName, cookieOptions);
    }

    public string? GetRefreshTokenFromCookie(HttpContext httpContext)
    {
        return httpContext.Request.Cookies.TryGetValue(_authOptions.RefreshCookieName, out var token)
            ? token
            : null;
    }

    public async Task<IEnumerable<RefreshTokens>> GetUserActiveSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null && rt.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(rt => rt.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsTokenReusedAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var revokedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.RevokedAtUtc != null, cancellationToken);

        return revokedToken is not null;
    }

    public async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_authOptions.RefreshTokenExpiryDays * 2);
        var expiredTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.ExpiresAtUtc < cutoffDate)
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count > 0)
        {
            _dbContext.RefreshTokens.RemoveRange(expiredTokens);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[RefreshToken.Cleanup] RemovedExpiredTokens={Count}",
                expiredTokens.Count);
        }
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(RefreshTokenEntropyBytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string? TruncateString(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
