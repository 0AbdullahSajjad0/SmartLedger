using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Common.Storage;

namespace SmartLedger.Infrastructure.Storage;

internal sealed class LocalFileStorage(IOptions<StorageOptions> options, IWebHostEnvironment environment) : IFileStorage
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly string _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.LocalPath));

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<StoredFile?>(null);
        }

        var contentType = ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";
        return Task.FromResult<StoredFile?>(new StoredFile(File.OpenRead(path), contentType));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var path = PathFor(folder);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key resolves outside the storage folder.", nameof(key));
        }
        return path;
    }
}
