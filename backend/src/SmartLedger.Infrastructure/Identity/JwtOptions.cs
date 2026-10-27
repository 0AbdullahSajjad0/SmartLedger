using System.ComponentModel.DataAnnotations;

namespace SmartLedger.Infrastructure.Identity;

/// <summary>Bound from the "Jwt" configuration section. The app refuses to start if it's invalid.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "";

    [Required]
    public string Audience { get; init; } = "";

    /// <summary>HMAC-SHA256 key. A secret: set via user secrets locally and a Container App secret in production.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = "";

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a session survives without use.</summary>
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(7);

    /// <summary>The same, when "Keep me signed in" was ticked.</summary>
    public TimeSpan RememberMeRefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);
}
