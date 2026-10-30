using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartLedger.Application.Account;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Application.Email;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Identity;

internal sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IFileStorage storage,
    IEmailOutbox outbox,
    TimeProvider timeProvider,
    ILogger<AccountService> logger) : IAccountService
{
    private static readonly Error UserNotFound = Error.NotFound("account.not_found", "The account no longer exists.");

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

    public async Task<Result<UserDto>> UpdateNotificationsAsync(
        Guid userId, UpdateNotificationsRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return UserNotFound;
        }

        user.BudgetAlerts = request.BudgetAlerts;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException($"Updating user {userId} failed: {Describe(updated)}");
        }

        return AuthService.ToDto(user);
    }

    public async Task<Result<bool>> SendConfirmationEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return UserNotFound;
        }

        var since = timeProvider.GetUtcNow() - AuthService.EmailCooldown;
        if (user.EmailConfirmed || await outbox.QueuedSinceAsync(userId, EmailKind.EmailConfirmation, since, cancellationToken))
        {
            return true;
        }

        outbox.Add(EmailKind.EmailConfirmation, userId);
        await db.SaveChangesAsync(cancellationToken);
        outbox.Wake();
        return true;
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

        var changed = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return Error.Validation("account.invalid_password", Describe(changed));
        }

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

        var deleted = await userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            throw new InvalidOperationException($"Deleting user {userId} failed: {Describe(deleted)}");
        }

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
