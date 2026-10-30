using Azure;
using Azure.Communication.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Email;
using AzureEmail = Azure.Communication.Email.EmailMessage;
using EmailMessage = SmartLedger.Application.Email.EmailMessage;

namespace SmartLedger.Infrastructure.Email;

/// <summary>
/// Azure Communication Services Email. The client signs in with the Container App's managed identity (no key or
/// connection string). Its own retries are off: the outbox worker decides when to try again.
/// </summary>
internal sealed class AzureEmailSender(EmailClient client, IOptions<EmailOptions> options) : IEmailSender
{
    private static readonly TimeSpan DefaultThrottleWait = TimeSpan.FromMinutes(1);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var email = new AzureEmail(
            options.Value.SenderAddress,
            message.To,
            new EmailContent(message.Subject) { PlainText = message.PlainText, Html = message.Html });

        try
        {
            // Started, not Completed: waiting for delivery would poll the service, and status checks have a quota too.
            await client.SendAsync(WaitUntil.Started, email, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            throw new EmailThrottledException(RetryAfter(ex) ?? DefaultThrottleWait, ex);
        }
    }

    private static TimeSpan? RetryAfter(RequestFailedException ex) =>
        ex.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true && int.TryParse(value, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
}

/// <summary>For development: saves each email as an HTML file to open in a browser, and logs where it went.</summary>
internal sealed class LocalEmailSender(
    IOptions<EmailOptions> options,
    IWebHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<LocalEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var folder = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.LocalPath));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"{timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(path, message.Html, cancellationToken);

        logger.LogInformation("Email to {To}, \"{Subject}\", saved to {Path}", message.To, message.Subject, path);
    }
}
