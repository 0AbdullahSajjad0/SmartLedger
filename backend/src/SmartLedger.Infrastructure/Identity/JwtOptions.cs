using System.ComponentModel.DataAnnotations;

namespace SmartLedger.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "";

    [Required]
    public string Audience { get; init; } = "";

    [Required, MinLength(32)]
    public string SigningKey { get; init; } = "";

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan RememberMeRefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);
}
