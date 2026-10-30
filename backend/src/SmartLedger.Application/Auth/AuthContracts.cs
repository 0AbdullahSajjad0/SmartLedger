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

public sealed record ResetPasswordRequest(
    Guid UserId,
    [Required, StringLength(2048)] string Token,
    [Required, StringLength(128, MinimumLength = 10)] string NewPassword);

public sealed record ConfirmEmailRequest(Guid UserId, [Required, StringLength(2048)] string Token);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string BaseCurrency, bool BudgetAlerts, bool EmailConfirmed);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);
