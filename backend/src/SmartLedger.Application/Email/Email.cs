namespace SmartLedger.Application.Email;

/// <param name="PlainText">For mail apps that don't show HTML; says everything the HTML says.</param>
public sealed record EmailMessage(string To, string Subject, string PlainText, string Html);

/// <summary>Hands an email to the mail service. Azure Communication Services in production, files on disk locally.</summary>
public interface IEmailSender
{
    /// <exception cref="EmailThrottledException">The mail service's sending limit was reached; try again later.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>The mail service refused for now because too many emails were sent recently.</summary>
public sealed class EmailThrottledException(TimeSpan retryAfter, Exception? innerException = null)
    : Exception($"Email sending limit reached; retry after {retryAfter}.", innerException)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public enum EmailKind
{
    /// <summary>A link to choose a new password. The link is made when the email is sent, not when it was asked for.</summary>
    PasswordReset = 1,

    /// <summary>A category reached 80% or 100% of its monthly budget. The reference is the budget alert.</summary>
    BudgetAlert = 2,

    /// <summary>A link that proves the inbox belongs to the account holder. Skipped if confirmed by the time it's sent.</summary>
    EmailConfirmation = 3,
}

/// <summary>
/// Emails waiting to be sent, kept in the database so none are lost when the app scales to zero or the mail service
/// asks us to slow down. A background worker sends them one at a time.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>
    /// Adds an email to the current unit of work: it is saved together with the caller's next SaveChanges, so the
    /// email exists only if the change that caused it was saved.
    /// </summary>
    void Add(EmailKind kind, Guid userId, Guid? referenceId = null);

    /// <summary>
    /// Whether an email of this kind was queued for the user after <paramref name="since"/>. Lets a button pressed
    /// again and again send one email, not many, and protects the mail service's small hourly quota.
    /// </summary>
    Task<bool> QueuedSinceAsync(Guid userId, EmailKind kind, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>After saving: tells the worker there is something new, so it doesn't wait for its next check.</summary>
    void Wake();
}
