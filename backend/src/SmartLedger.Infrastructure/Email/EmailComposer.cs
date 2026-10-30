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

/// <summary>Writes an outbox email at the moment it is sent, from what is true then.</summary>
internal sealed class EmailComposer(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<EmailOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>A reset link lives this long, and a request older than this is not worth answering any more.</summary>
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    /// <summary>Null when there is nothing to send any more.</summary>
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
        // Confirmed in the meantime (e.g. through a password reset link), or asked for so long ago that a newer
        // request has surely been sent.
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

    /// <summary>Identity tokens contain "+", "/" and "="; Base64Url makes them safe in a link.</summary>
    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private async Task<EmailMessage?> PasswordResetAsync(ApplicationUser user, OutgoingEmail email)
    {
        // The user asked over an hour ago and has probably asked again since; a late link would only confuse.
        if (timeProvider.GetUtcNow() - email.CreatedAt > PasswordResetLifetime)
        {
            return null;
        }

        // Made now, so its hour starts when it is sent. It stops working once used: resetting the password renews the
        // account's security stamp, which the token is tied to.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        // The token goes after "#": browsers never send that part to a server, so it stays out of hosting logs and
        // the Referer header.
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
        // Turned off since the alert was recorded: respect the latest choice. Unconfirmed addresses get no alerts, so
        // an account opened with someone else's address can't be used to send them mail.
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
        // Rounded like the Categories page shows it, so the email and the page say the same number.
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

    /// <summary>"USD 1,640.00": the same way the app's AI analysis writes amounts.</summary>
    private static string Money(string currency, decimal amount) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
