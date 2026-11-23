namespace Paperless.Api.Storage;

// ############################
// Storage abstraction shared by local development and object-storage deployments.
// ############################
public interface IDocumentStorage
{
    Task PutAsync(string key, Stream content, long size, string contentType, CancellationToken cancellationToken);
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
