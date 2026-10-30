using SmartLedger.Application.Email;

namespace SmartLedger.Infrastructure.Email;

public class OutgoingEmail
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public EmailKind Kind { get; set; }

    public Guid? ReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? GaveUpAt { get; set; }

    public string? LastError { get; set; }
}
