using Microsoft.AspNetCore.Http;

namespace Services.DTOs;

public class AuthTokenOptions
{
    public const string SectionName = "Auth";

    public int RefreshTokenExpiryDays { get; set; } = 30;
    public string RefreshCookieName { get; set; } = "refresh-token";

    private CookieSecurePolicy _securePolicy = CookieSecurePolicy.Always;
    public string CookieSecurePolicySetting
    {
        get => _securePolicy switch
        {
            CookieSecurePolicy.Always => "Always",
            CookieSecurePolicy.SameAsRequest => "SameAsRequest",
            CookieSecurePolicy.None => "None",
            _ => "Always"
        };
        set
        {
            _securePolicy = value?.ToLowerInvariant() switch
            {
                "always" => CookieSecurePolicy.Always,
                "sameasrequest" => CookieSecurePolicy.SameAsRequest,
                "none" => CookieSecurePolicy.None,
                _ => CookieSecurePolicy.Always
            };
        }
    }

    private SameSiteMode _sameSiteMode = SameSiteMode.None;
    public string CookieSameSiteModeSetting
    {
        get => _sameSiteMode switch
        {
            SameSiteMode.None => "None",
            SameSiteMode.Lax => "Lax",
            SameSiteMode.Strict => "Strict",
            _ => "None"
        };
        set
        {
            _sameSiteMode = value?.ToLowerInvariant() switch
            {
                "none" => SameSiteMode.None,
                "lax" => SameSiteMode.Lax,
                "strict" => SameSiteMode.Strict,
                _ => SameSiteMode.None
            };
        }
    }

    public CookieSecurePolicy GetSecurePolicy() => _securePolicy;
    public SameSiteMode GetSameSiteMode() => _sameSiteMode;
}

public class JwtTokenOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "HotWaterGas";
    public string Audience { get; set; } = "HotWaterGasClient";
    public int AccessTokenExpiryMinutes { get; set; } = 60;
}
