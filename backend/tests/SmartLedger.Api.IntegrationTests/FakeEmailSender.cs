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

    /// <param name="where">Narrows to one kind of email, e.g. <see cref="IsBudgetAlert"/>.</param>
    public IReadOnlyList<EmailMessage> SentTo(string address, Func<EmailMessage, bool>? where = null) =>
        _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase) && (where?.Invoke(m) ?? true)).ToList();

    /// <summary>Waits for the background worker to send the <paramref name="count"/>th matching email to the address.</summary>
    public async Task<EmailMessage> WaitForAsync(string address, Func<EmailMessage, bool>? where = null, int count = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var sent = SentTo(address, where);
            if (sent.Count >= count)
            {
                return sent[count - 1];
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} matching email(s) to {address}, got {sent.Count}.");
            }
            await Task.Delay(50);
        }
    }

    public static bool IsConfirmation(EmailMessage email) => email.Subject == "Confirm your email for SmartLedger";

    public static bool IsPasswordReset(EmailMessage email) => email.Subject == "Reset your SmartLedger password";

    public static bool IsBudgetAlert(EmailMessage email) => email.Html.Contains(">Budget alert<", StringComparison.Ordinal);
}
