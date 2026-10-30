namespace SmartLedger.Application.Email;

public sealed record EmailMessage(string To, string Subject, string PlainText, string Html);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class EmailThrottledException(TimeSpan retryAfter, Exception? innerException = null)
    : Exception($"Email sending limit reached; retry after {retryAfter}.", innerException)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public enum EmailKind
{
    PasswordReset = 1,

    BudgetAlert = 2,

    EmailConfirmation = 3,
}

public interface IEmailOutbox
{
    void Add(EmailKind kind, Guid userId, Guid? referenceId = null);

    Task<bool> QueuedSinceAsync(Guid userId, EmailKind kind, DateTimeOffset since, CancellationToken cancellationToken);

    void Wake();
}
