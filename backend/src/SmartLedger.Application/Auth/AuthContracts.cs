using System.ComponentModel.DataAnnotations;

namespace SmartLedger.Application.Auth;

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 10)] string Password,
    [Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Use a three-letter ISO currency code, e.g. USD.")] string BaseCurrency);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    bool RememberMe);

public sealed record RefreshRequest([Required] string RefreshToken);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string BaseCurrency);

/// <param name="AccessToken">Short-lived JWT, sent as "Authorization: Bearer …".</param>
/// <param name="RefreshToken">Long-lived, single-use token for getting the next access token.</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);
