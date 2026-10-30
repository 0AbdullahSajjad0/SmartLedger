using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.Email;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Email;

internal sealed class EmailOutbox(AppDbContext db, EmailSignal signal, TimeProvider timeProvider) : IEmailOutbox
{
    public void Add(EmailKind kind, Guid userId, Guid? referenceId = null)
    {
        var now = timeProvider.GetUtcNow();
        db.OutgoingEmails.Add(new OutgoingEmail
        {
            UserId = userId,
            Kind = kind,
            ReferenceId = referenceId,
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }

    public Task<bool> QueuedSinceAsync(Guid userId, EmailKind kind, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.OutgoingEmails.AnyAsync(e => e.UserId == userId && e.Kind == kind && e.CreatedAt > since, cancellationToken);

    public void Wake() => signal.Wake();
}

/// <summary>Wakes the email worker early. Several wakes before it looks count as one.</summary>
internal sealed class EmailSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Wake() => _channel.Writer.TryWrite(true);

    /// <summary>Returns when woken or after <paramref name="timeout"/>, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out: time for a regular look at the outbox.
        }
    }
}
