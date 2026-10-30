using System.ComponentModel.DataAnnotations;

namespace SmartLedger.Infrastructure.Storage;

public enum StorageProvider
{
    Local,

    AzureBlob,
}

public sealed class StorageOptions : IValidatableObject
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; init; } = StorageProvider.AzureBlob;

    public string LocalPath { get; init; } = "App_Data/receipts";

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
