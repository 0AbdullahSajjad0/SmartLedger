namespace SmartLedger.Application.Common.Storage;

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);

    Task DeleteFolderAsync(string folder, CancellationToken cancellationToken);
}

public sealed record StoredFile(Stream Content, string ContentType);
