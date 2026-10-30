using SmartLedger.Application.Common;

namespace SmartLedger.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    Task<Result<UserDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);

    Task<Result<UserDto>> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);

    Task<Result<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
