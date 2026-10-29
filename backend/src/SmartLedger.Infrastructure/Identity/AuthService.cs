using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Common;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Identity;

internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    TokenService tokenService,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IAuthService
{
    // The same message for "no such email" and "wrong password", so the login form can't be used to find accounts.
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Email or password is incorrect.");

    private static readonly Error LockedOut =
        Error.Unauthorized("auth.locked_out", "Too many failed attempts. Try again in 15 minutes.");

    private static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "Your session has ended. Please sign in again.");

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        // The user, their default categories and the first refresh token are saved together or not at all.
        // The execution strategy reruns the whole block if Azure SQL drops the connection (e.g. while resuming).
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<Result<AuthResponse>>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName.Trim(),
                BaseCurrency = request.BaseCurrency,
            };

            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return created.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail")
                    ? Error.Conflict("auth.email_taken", "An account with this email already exists.")
                    : Error.Validation("auth.invalid_registration", string.Join(" ", created.Errors.Select(e => e.Description)));
            }

            db.Categories.AddRange(DefaultCategories.For(user.Id));
            var response = await IssueTokensAsync(user, rememberMe: false, familyId: Guid.CreateVersion7(), replacing: null, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return response;
        });
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return InvalidCredentials;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return LockedOut;
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Counts towards the lockout configured in DependencyInjection (5 attempts, 15 minutes).
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user) ? LockedOut : InvalidCredentials;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return await IssueTokensAsync(user, request.RememberMe, familyId: Guid.CreateVersion7(), replacing: null, cancellationToken);
    }

    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var hash = TokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return InvalidRefreshToken;
        }

        if (stored.RevokedAt is not null)
        {
            // A token that was already exchanged is being used again: someone has a copy.
            // End every session that came from the same sign-in, the thief's and the user's.
            if (stored.ReplacedByTokenId is not null)
            {
                await db.RefreshTokens
                    .Where(t => t.FamilyId == stored.FamilyId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
            }

            return InvalidRefreshToken;
        }

        if (stored.ExpiresAt <= now)
        {
            return InvalidRefreshToken;
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return InvalidRefreshToken;
        }

        return await IssueTokensAsync(user, stored.RememberMe, stored.FamilyId, replacing: stored, cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = TokenService.HashRefreshToken(refreshToken);

        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, timeProvider.GetUtcNow()), cancellationToken);
    }

    public async Task<Result<UserDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());

        return user is null
            ? Error.NotFound("auth.user_not_found", "The account no longer exists.")
            : ToDto(user);
    }

    private async Task<AuthResponse> IssueTokensAsync(
        ApplicationUser user,
        bool rememberMe,
        Guid familyId,
        RefreshToken? replacing,
        CancellationToken cancellationToken)
    {
        var jwt = jwtOptions.Value;
        var now = timeProvider.GetUtcNow();

        var (accessToken, accessExpiresAt) = tokenService.CreateAccessToken(user);
        var (refreshToken, refreshHash) = TokenService.CreateRefreshToken();

        // Each refresh starts the inactivity window again, so an active user stays signed in.
        var lifetime = rememberMe ? jwt.RememberMeRefreshTokenLifetime : jwt.RefreshTokenLifetime;

        var stored = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            FamilyId = familyId,
            RememberMe = rememberMe,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };
        db.RefreshTokens.Add(stored);

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedByTokenId = stored.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new AuthResponse(accessToken, accessExpiresAt, refreshToken, stored.ExpiresAt, ToDto(user));
    }

    internal static UserDto ToDto(ApplicationUser user) =>
        new(user.Id, user.Email!, user.DisplayName, user.BaseCurrency);
}
