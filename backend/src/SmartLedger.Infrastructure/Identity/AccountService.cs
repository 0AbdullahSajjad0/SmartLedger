using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartLedger.Application.Account;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Identity;

internal sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IFileStorage storage,
    TimeProvider timeProvider,
    ILogger<AccountService> logger) : IAccountService
{
    private static readonly Error UserNotFound = Error.NotFound("account.not_found", "The account no longer exists.");

    // 400, not 401: the session is fine, only the password typed into the form is wrong. The app treats a 401 as
    // "signed out" and would end the session.
    private static readonly Error WrongPassword = Error.Validation("account.wrong_password", "That password isn't right.");

    private static readonly Error LockedOut =
        Error.Validation("account.locked_out", "Too many failed attempts. Try again in 15 minutes.");

    public async Task<Result<UserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName.Trim();
        if (displayName.Length == 0)
        {
            return Error.Validation("account.name_required", "Enter the name to show in the app.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return UserNotFound;
        }

        user.DisplayName = displayName;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException($"Updating user {userId} failed: {Describe(updated)}");
        }

        return AuthService.ToDto(user);
    }

    public async Task<Result<bool>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return UserNotFound;
        }

        if (await VerifyPasswordAsync(user, request.CurrentPassword) is { } error)
        {
            return error;
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return Error.Validation("account.same_password", "Choose a password different from your current one.");
        }

        // Also renews the security stamp, which makes any password reset link sent earlier stop working.
        var changed = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return Error.Validation("account.invalid_password", Describe(changed));
        }

        // Whoever knew the old password may be signed in somewhere: those sessions end, this one stays.
        await RevokeOtherSessionsAsync(userId, request.RefreshToken, cancellationToken);
        return true;
    }

    public Task SignOutOtherDevicesAsync(Guid userId, string? refreshToken, CancellationToken cancellationToken) =>
        RevokeOtherSessionsAsync(userId, refreshToken, cancellationToken);

    public async Task<Result<bool>> DeleteAsync(Guid userId, DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return UserNotFound;
        }

        if (await VerifyPasswordAsync(user, request.Password) is { } error)
        {
            return error;
        }

        // The database removes everything that belongs to the user along with it (cascading deletes): receipts and
        // their line items, categories, monthly analyses and sessions.
        var deleted = await userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            throw new InvalidOperationException($"Deleting user {userId} failed: {Describe(deleted)}");
        }

        // Then the files, as when deleting one receipt: a file that fails to delete is only wasted storage, never a
        // record pointing at nothing. The whole folder goes, previews and anything an earlier failure left behind
        // included. Not cancelled when the browser disconnects: the account is already gone.
        try
        {
            await storage.DeleteFolderAsync(userId.ToString(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Account {UserId} was deleted but its files were not; delete the folder {Folder} from storage",
                userId, userId);
        }

        logger.LogInformation("Account {UserId} deleted at the user's request", userId);
        return true;
    }

    /// <summary>
    /// Asks for the password again before changes that matter. Wrong guesses count towards the same lockout as signing
    /// in, so a session left open on a shared computer can't be used to try passwords.
    /// </summary>
    private async Task<Error?> VerifyPasswordAsync(ApplicationUser user, string password)
    {
        if (await userManager.IsLockedOutAsync(user))
        {
            return LockedOut;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user) ? LockedOut : WrongPassword;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return null;
    }

    /// <summary>
    /// Revokes every refresh token of the user except those from the same sign-in as <paramref name="keepRefreshToken"/>.
    /// The family is kept rather than the token itself, because the browser may have just exchanged that token.
    /// </summary>
    private async Task RevokeOtherSessionsAsync(Guid userId, string? keepRefreshToken, CancellationToken cancellationToken)
    {
        var sessions = db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null);

        if (!string.IsNullOrEmpty(keepRefreshToken))
        {
            var hash = TokenService.HashRefreshToken(keepRefreshToken);
            var keep = await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.UserId == userId)
                .Select(t => (Guid?)t.FamilyId)
                .SingleOrDefaultAsync(cancellationToken);

            if (keep is { } familyId)
            {
                sessions = sessions.Where(t => t.FamilyId != familyId);
            }
        }

        await sessions.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, timeProvider.GetUtcNow()), cancellationToken);
    }

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));
}
