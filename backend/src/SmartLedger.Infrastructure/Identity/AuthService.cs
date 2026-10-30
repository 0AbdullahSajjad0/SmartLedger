using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Common;
using SmartLedger.Application.Email;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Identity;

internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    TokenService tokenService,
    IOptions<JwtOptions> jwtOptions,
    IEmailOutbox outbox,
    TimeProvider timeProvider) : IAuthService
{
    // The same message for "no such email" and "wrong password", so the login form can't be used to find accounts.
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Email or password is incorrect.");

    private static readonly Error LockedOut =
        Error.Unauthorized("auth.locked_out", "Too many failed attempts. Try again in 15 minutes.");

    private static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "Your session has ended. Please sign in again.");

    private static readonly Error InvalidResetLink = Error.Validation(
        "auth.invalid_reset_link", "This link has expired or was already used. Ask for a new one.");

    private static readonly Error InvalidConfirmationLink = Error.Validation(
        "auth.invalid_confirmation_link", "This link has expired or isn't complete. Send a new one from your Account page.");

    /// <summary>A second reset or confirmation email for the same account is not sent sooner than this.</summary>
    internal static readonly TimeSpan EmailCooldown = TimeSpan.FromMinutes(5);

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
            // Saved by IssueTokensAsync below, in the same transaction: no account without its confirmation email.
            outbox.Add(EmailKind.EmailConfirmation, user.Id);
            var response = await IssueTokensAsync(user, rememberMe: false, familyId: Guid.CreateVersion7(), replacing: null, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            outbox.Wake();
            return response;
        });
    }

    public async Task<Result<UserDto>> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || DecodeToken(request.Token) is not { } token)
        {
            return InvalidConfirmationLink;
        }

        // Opening the link twice (or after a reset already confirmed the address) is fine: it's done either way.
        if (user.EmailConfirmed)
        {
            return ToDto(user);
        }

        var confirmed = await userManager.ConfirmEmailAsync(user, token);
        return confirmed.Succeeded ? ToDto(user) : InvalidConfirmationLink;
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

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return;
        }

        // Someone pressing the button repeatedly (or using it to flood an inbox) gets one email, not many. It also
        // protects the mail service's small hourly quota, which every user shares.
        var since = timeProvider.GetUtcNow() - EmailCooldown;
        if (await outbox.QueuedSinceAsync(user.Id, EmailKind.PasswordReset, since, cancellationToken))
        {
            return;
        }

        outbox.Add(EmailKind.PasswordReset, user.Id);
        await db.SaveChangesAsync(cancellationToken);
        outbox.Wake();
    }

    public async Task<Result<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || DecodeToken(request.Token) is not { } token)
        {
            return InvalidResetLink;
        }

        var reset = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!reset.Succeeded)
        {
            return reset.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? InvalidResetLink
                : Error.Validation("auth.invalid_password", string.Join(" ", reset.Errors.Select(e => e.Description)));
        }

        // Whoever knew the old password is signed out everywhere, and failed guesses no longer block the owner.
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, timeProvider.GetUtcNow()), cancellationToken);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        // The link arrived in this inbox and was used, which proves the address as well as a confirmation link would.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        return true;
    }

    private static string? DecodeToken(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static UserDto ToDto(ApplicationUser user) =>
        new(user.Id, user.Email!, user.DisplayName, user.BaseCurrency, user.BudgetAlerts, user.EmailConfirmed);
}
