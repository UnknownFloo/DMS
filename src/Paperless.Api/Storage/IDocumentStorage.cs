namespace Paperless.Api.Storage;

public interface IDocumentStorage
{
    Task PutAsync(string key, Stream content, long size, string contentType, CancellationToken cancellationToken);
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}