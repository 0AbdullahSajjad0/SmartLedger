namespace SmartLedger.Application.Common.Storage;

/// <summary>Stores original receipt files. Azure Blob Storage in production, a local folder in development.</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Null when no file exists under the key.</summary>
    Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes every file whose key starts with "<paramref name="folder"/>/", e.g. all of one user's files.</summary>
    Task DeleteFolderAsync(string folder, CancellationToken cancellationToken);
}

/// <param name="Content">The caller disposes it.</param>
public sealed record StoredFile(Stream Content, string ContentType);
