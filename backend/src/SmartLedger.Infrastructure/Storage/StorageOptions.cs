using System.ComponentModel.DataAnnotations;

namespace SmartLedger.Infrastructure.Storage;

public enum StorageProvider
{
    /// <summary>A folder on disk: for development and tests.</summary>
    Local,

    /// <summary>Azure Blob Storage, signed in with the app's managed identity.</summary>
    AzureBlob,
}

/// <summary>Bound from the "Storage" configuration section. The app refuses to start if it's invalid.</summary>
public sealed class StorageOptions : IValidatableObject
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; init; } = StorageProvider.AzureBlob;

    /// <summary>Local: the folder, absolute or relative to the app's content root.</summary>
    public string LocalPath { get; init; } = "App_Data/receipts";

    /// <summary>AzureBlob: e.g. https://stsmartledger.blob.core.windows.net. Not a secret: access needs the identity.</summary>
    public string? BlobServiceUri { get; init; }

    public string ContainerName { get; init; } = "receipts";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == StorageProvider.AzureBlob && !Uri.TryCreate(BlobServiceUri, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                "Storage:BlobServiceUri must be the storage account's blob address when Storage:Provider is AzureBlob.",
                [nameof(BlobServiceUri)]);
        }
    }
}
