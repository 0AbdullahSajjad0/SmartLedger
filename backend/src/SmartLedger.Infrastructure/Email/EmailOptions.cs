using System.ComponentModel.DataAnnotations;

namespace SmartLedger.Infrastructure.Email;

public enum EmailProvider
{
    Local,

    AzureCommunicationServices,
}

public sealed class EmailOptions : IValidatableObject
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; init; } = EmailProvider.AzureCommunicationServices;

    public string? AppUrl { get; init; }

    public string? Endpoint { get; init; }

    public string? SenderAddress { get; init; }

    public string LocalPath { get; init; } = "App_Data/emails";

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
