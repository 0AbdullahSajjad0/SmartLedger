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
}
