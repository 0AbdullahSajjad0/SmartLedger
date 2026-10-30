using SmartLedger.Application.Email;

namespace SmartLedger.Infrastructure.Email;

/// <summary>
/// One email in the outbox. Only what it is about is stored; the text (and a password reset link) is written when it
/// is sent, so no live reset link ever sits in the database.
/// </summary>
public class OutgoingEmail
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public EmailKind Kind { get; set; }

    /// <summary>What the email is about, e.g. the budget alert's id. Null for a password reset.</summary>
    public Guid? ReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Not sent before this; moved later when the mail service asks us to wait or a try fails.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Failed tries, not counting waits the mail service asked for.</summary>
    public int Attempts { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Set when it will never be sent: too many failures, or nothing left to say (e.g. a stale reset request).</summary>
    public DateTimeOffset? GaveUpAt { get; set; }

    public string? LastError { get; set; }
}
