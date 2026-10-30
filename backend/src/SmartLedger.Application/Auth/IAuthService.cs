using SmartLedger.Application.Common;

namespace SmartLedger.Application.Auth;

public interface IAuthService
{
    /// <summary>Creates the account with the default categories and signs it in.</summary>
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for new tokens. The old refresh token stops working.</summary>
    Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the refresh token. Already-issued access tokens expire on their own within minutes.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    Task<Result<UserDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Emails a reset link if an account uses the address. Says nothing either way, so the form can't be used to find
    /// out who has an account. At most one email per account every few minutes.
    /// </summary>
    Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Sets a new password from a reset link, signs out every device and lifts any lockout.</summary>
    Task<Result<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
