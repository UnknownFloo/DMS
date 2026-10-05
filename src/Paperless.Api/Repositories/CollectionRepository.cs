using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.Entities;

namespace Paperless.Api.Repositories;

public class CollectionRepository(PaperlessDbContext db) : ICollectionRepository
{
    public Task<Collection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Collections.Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Collections.Include(x => x.Documents).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public Task AddAsync(Collection collection, CancellationToken cancellationToken = default) => db.Collections.AddAsync(collection, cancellationToken).AsTask();

    public Task DeleteAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        db.Collections.Remove(collection);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
