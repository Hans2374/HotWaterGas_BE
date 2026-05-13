using System;

namespace Repos.Models;

public class RefreshTokens
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    public string? RevokedByIp { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceInfo { get; set; }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired => DateTime.UtcNow > ExpiresAtUtc;

    public Guid? ParentTokenId { get; set; }

    public Guid TokenFamilyId { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    public virtual Users User { get; set; } = null!;

    public virtual RefreshTokens? ParentToken { get; set; }
}
