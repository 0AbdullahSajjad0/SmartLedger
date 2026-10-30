using System.Collections.Concurrent;
using SmartLedger.Application.Email;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>Keeps every "sent" email in memory, so tests can read them. Shared by all tests; filter by address.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> SentTo(string address) =>
        _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Waits for the background worker to send the <paramref name="count"/>th email to the address.</summary>
    public async Task<EmailMessage> WaitForAsync(string address, int count = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var sent = SentTo(address);
            if (sent.Count >= count)
            {
                return sent[count - 1];
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} email(s) to {address}, got {sent.Count}.");
            }
            await Task.Delay(50);
        }
    }
}
