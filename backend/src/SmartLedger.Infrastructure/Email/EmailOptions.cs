using System.ComponentModel.DataAnnotations;

namespace SmartLedger.Infrastructure.Email;

public enum EmailProvider
{
    /// <summary>Saves each email as an HTML file instead of sending it: for development.</summary>
    Local,

    /// <summary>Azure Communication Services Email, signed in with the app's managed identity.</summary>
    AzureCommunicationServices,
}

/// <summary>Bound from the "Email" configuration section. The app refuses to start if it's invalid.</summary>
public sealed class EmailOptions : IValidatableObject
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; init; } = EmailProvider.AzureCommunicationServices;

    /// <summary>
    /// The web app's address, used for links in emails, e.g. https://red-coast-0ea7ce800.5.azurestaticapps.net.
    /// </summary>
    public string? AppUrl { get; init; }

    /// <summary>AzureCommunicationServices: the resource's endpoint, e.g. https://acs-smartledger.india.communication.azure.com.</summary>
    public string? Endpoint { get; init; }

    /// <summary>AzureCommunicationServices: the "MailFrom" address of the connected domain, e.g. DoNotReply@….azurecomm.net.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Local: the folder emails are saved to, absolute or relative to the app's content root.</summary>
    public string LocalPath { get; init; } = "App_Data/emails";

    /// <summary>
    /// Pause after each email. Azure's managed email domain accepts 5 emails a minute (and 10 an hour), so one every
    /// 12 seconds stays inside the first limit; the hourly one is handled by waiting when the service says so.
    /// </summary>
    public TimeSpan DelayBetweenEmails { get; init; } = TimeSpan.FromSeconds(12);

    public Uri AppUri => new(AppUrl!.TrimEnd('/') + "/");

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(AppUrl, UriKind.Absolute, out var app) || app.Scheme is not ("https" or "http"))
        {
            yield return new ValidationResult(
                "Email:AppUrl must be the web app's address (used for links in emails).", [nameof(AppUrl)]);
        }

        if (Provider == EmailProvider.AzureCommunicationServices)
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
            {
                yield return new ValidationResult(
                    "Email:Endpoint must be the Communication Services resource's endpoint.", [nameof(Endpoint)]);
            }

            if (string.IsNullOrWhiteSpace(SenderAddress) || !SenderAddress.Contains('@'))
            {
                yield return new ValidationResult(
                    "Email:SenderAddress must be the connected email domain's sender address.", [nameof(SenderAddress)]);
            }
        }
    }
}
