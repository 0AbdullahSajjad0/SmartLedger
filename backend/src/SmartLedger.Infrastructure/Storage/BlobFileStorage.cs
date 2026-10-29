using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SmartLedger.Application.Common.Storage;

namespace SmartLedger.Infrastructure.Storage;

/// <summary>Azure Blob Storage. The container is private; the API streams files to signed-in owners only.</summary>
internal sealed class BlobFileStorage(BlobContainerClient container) : IFileStorage
{
    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var download = await container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new StoredFile(download.Value.Content, download.Value.Details.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task DeleteFolderAsync(string folder, CancellationToken cancellationToken)
    {
        // Blob storage has no real folders: list the blobs whose names start with the prefix and delete each.
        var blobs = container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: $"{folder.TrimEnd('/')}/", cancellationToken);
        await foreach (var blob in blobs)
        {
            await container.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
    }
}
