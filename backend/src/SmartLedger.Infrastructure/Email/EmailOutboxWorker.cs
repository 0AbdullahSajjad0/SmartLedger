using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Email;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Email;

/// <summary>
/// Sends the outbox one email at a time, paced for the mail service's limits. When the service says "too many", the
/// email waits for as long as it asks, without counting as a failure. Runs in the API's only replica (max replicas is
/// 1), so no two workers pick the same email.
/// </summary>
internal sealed class EmailOutboxWorker(
    EmailSignal signal,
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    /// <summary>How long to wait between looks when nothing wakes the worker.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(5);

    /// <summary>Waits after the 1st, 2nd, 3rd and 4th failed try; the 5th failure gives up.</summary>
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                wait = await SendDueAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // E.g. the database is resuming. Must not stop the worker for good.
                logger.LogWarning(ex, "Couldn't work through the email outbox; trying again in a minute");
                wait = TimeSpan.FromMinutes(1);
            }

            await signal.WaitAsync(wait, stoppingToken);
        }
    }

    /// <summary>Sends every email that is due. Returns how long until the next one is.</summary>
    private async Task<TimeSpan> SendDueAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = timeProvider.GetUtcNow();

            var pending = db.OutgoingEmails.Where(e => e.SentAt == null && e.GaveUpAt == null);
            var email = await pending
                .Where(e => e.NextAttemptAt <= now)
                .OrderBy(e => e.NextAttemptAt)
                .FirstOrDefaultAsync(stoppingToken);

            if (email is null)
            {
                var next = await pending.MinAsync(e => (DateTimeOffset?)e.NextAttemptAt, stoppingToken);
                return next is null ? IdleCheck : Clamp(next.Value - now);
            }

            await SendAsync(scope.ServiceProvider, email, stoppingToken);
            await db.SaveChangesAsync(stoppingToken);

            if (email.SentAt is not null)
            {
                await Task.Delay(options.Value.DelayBetweenEmails, timeProvider, stoppingToken);
            }
        }
    }

    private async Task SendAsync(IServiceProvider services, OutgoingEmail email, CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();
        try
        {
            var message = await services.GetRequiredService<EmailComposer>().ComposeAsync(email, stoppingToken);
            if (message is null)
            {
                // Nothing to send any more: the account or alert is gone, alerts were turned off, or a reset request
                // is too old to be useful.
                email.GaveUpAt = now;
                email.LastError = "Nothing to send.";
                return;
            }

            await services.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
            email.SentAt = timeProvider.GetUtcNow();
            email.LastError = null;
            logger.LogInformation("Sent {Kind} email {EmailId}", email.Kind, email.Id);
        }
        catch (EmailThrottledException ex)
        {
            email.NextAttemptAt = now + ex.RetryAfter;
            logger.LogInformation("Email sending limit reached; {EmailId} waits {RetryAfter}", email.Id, ex.RetryAfter);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            email.Attempts++;
            email.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            if (email.Attempts > Backoff.Length)
            {
                email.GaveUpAt = now;
                logger.LogError(ex, "Gave up sending {Kind} email {EmailId} after {Attempts} tries", email.Kind, email.Id, email.Attempts);
            }
            else
            {
                email.NextAttemptAt = now + Backoff[email.Attempts - 1];
                logger.LogWarning(ex, "Sending {Kind} email {EmailId} failed; trying again later", email.Kind, email.Id);
            }
        }
    }

    private static TimeSpan Clamp(TimeSpan wait) =>
        wait < TimeSpan.Zero ? TimeSpan.Zero : wait > IdleCheck ? IdleCheck : wait;
}
