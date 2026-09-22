using Paperless.Api.Entities;

namespace Paperless.Api.Repositories;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Collection collection, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
