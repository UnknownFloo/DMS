namespace Paperless.Api.Storage;

public sealed class LocalDocumentStorage(string rootPath) : IDocumentStorage
{
    public async Task PutAsync(string key, Stream content, long size, string contentType, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = File.Create(path);
        await content.CopyToAsync(target, cancellationToken);
    }

    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolvePath(string key) => Path.IsPathRooted(key)
        ? key
        : Path.Combine(rootPath, key.Replace('/', Path.DirectorySeparatorChar));
}