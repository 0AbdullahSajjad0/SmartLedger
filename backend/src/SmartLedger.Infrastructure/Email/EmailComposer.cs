using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Common;
using SmartLedger.Application.Email;
using SmartLedger.Domain.Enums;
using SmartLedger.Infrastructure.Identity;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Email;

internal sealed class EmailComposer(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<EmailOptions> options,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    public async Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(email.UserId.ToString());
        if (user?.Email is null)
        {
            return null;
        }

        return email.Kind switch
        {
            EmailKind.PasswordReset => await PasswordResetAsync(user, email),
            EmailKind.BudgetAlert => await BudgetAlertAsync(user, email.ReferenceId, cancellationToken),
            EmailKind.EmailConfirmation => await EmailConfirmationAsync(user, email),
            _ => null,
        };
    }

    private async Task<EmailMessage?> EmailConfirmationAsync(ApplicationUser user, OutgoingEmail email)
    {
        if (user.EmailConfirmed || timeProvider.GetUtcNow() - email.CreatedAt > EmailConfirmationTokenProviderOptions.Lifetime)
        {
            return null;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = new Uri(options.Value.AppUri, $"confirm-email?user={user.Id}#token={Encode(token)}");

        return EmailTemplate.Render(
            user.Email!,
            "Confirm your email for SmartLedger",
            "Welcome",
            "Confirm your email address",
            [
                $"Thanks for opening a SmartLedger account, {user.DisplayName}. Please confirm that {user.Email} is your address.",
                "Until then, SmartLedger won't send you budget alerts. The link works for three days.",
                "If you didn't open an account, ignore this email and nothing more will be sent to you.",
            ],
            ("Confirm my email", link),
            "You're receiving this because this address was used to open a SmartLedger account.");
    }

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private async Task<EmailMessage?> PasswordResetAsync(ApplicationUser user, OutgoingEmail email)
    {
        if (timeProvider.GetUtcNow() - email.CreatedAt > PasswordResetLifetime)
        {
            return null;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var link = new Uri(options.Value.AppUri, $"reset-password?user={user.Id}#token={Encode(token)}");

        return EmailTemplate.Render(
            user.Email!,
            "Reset your SmartLedger password",
            "Password reset",
            "Choose a new password",
            [
                $"Someone, hopefully you, asked to reset the password of the SmartLedger account {user.Email}.",
                "The button below lets you choose a new one. It works once, within an hour. Every device signed in to the account will be signed out.",
                "If you didn't ask, you can ignore this email: your password stays as it is.",
            ],
            ("Choose a new password", link),
            "You're receiving this because a password reset was requested on SmartLedger.");
    }

    private async Task<EmailMessage?> BudgetAlertAsync(ApplicationUser user, Guid? alertId, CancellationToken cancellationToken)
    {
        if (!user.BudgetAlerts || !user.EmailConfirmed)
        {
            return null;
        }

        var alert = await db.BudgetAlerts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var category = await db.Categories
            .Where(c => c.Id == alert.CategoryId)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (category is null)
        {
            return null;
        }

        var month = new DateOnly(alert.Year, alert.Month, 1);
        var monthName = month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var shortMonth = month.ToString("MMMM", CultureInfo.InvariantCulture);
        var spent = Money(user.BaseCurrency, alert.Spent);
        var budget = Money(user.BaseCurrency, alert.Budget);
        var percent = (int)Math.Round(alert.Spent / alert.Budget * 100, MidpointRounding.AwayFromZero);
        var link = new Uri(options.Value.AppUri, $"categories?month={Months.Format(month)}");

        var (subject, title, detail) = alert.Level == BudgetAlertLevel.Exceeded
            ? ($"{category} is over its {shortMonth} budget",
                $"{category} is over budget",
                $"That's {Money(user.BaseCurrency, alert.Spent - alert.Budget)} more than you planned.")
            : ($"{category} has used {percent}% of its {shortMonth} budget",
                $"{category} is at {percent}% of its budget",
                $"That leaves {Money(user.BaseCurrency, alert.Budget - alert.Spent)} for the rest of the month.");

        return EmailTemplate.Render(
            user.Email!,
            subject,
            "Budget alert",
            title,
            [$"You've spent {spent} of your {budget} budget for {category} in {monthName}.", detail],
            ("See your budgets", link),
            "You get these because budget alerts are on. You can turn them off on your Account page in SmartLedger.");
    }

    private static string Money(string currency, decimal amount) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
