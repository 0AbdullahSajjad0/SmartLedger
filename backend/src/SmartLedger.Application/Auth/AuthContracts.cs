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

public sealed record ForgotPasswordRequest([Required, EmailAddress, StringLength(256)] string Email);

/// <param name="UserId">From the link in the email.</param>
/// <param name="Token">From the link in the email, as it appears there (Base64Url).</param>
public sealed record ResetPasswordRequest(
    Guid UserId,
    [Required, StringLength(2048)] string Token,
    [Required, StringLength(128, MinimumLength = 10)] string NewPassword);

/// <param name="BudgetAlerts">Whether the user gets an email when a category reaches 80% and 100% of its budget.</param>
public sealed record UserDto(Guid Id, string Email, string DisplayName, string BaseCurrency, bool BudgetAlerts);

/// <param name="AccessToken">Short-lived JWT, sent as "Authorization: Bearer …".</param>
/// <param name="RefreshToken">Long-lived, single-use token for getting the next access token.</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);
