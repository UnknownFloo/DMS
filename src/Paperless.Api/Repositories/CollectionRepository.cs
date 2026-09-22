using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.Entities;

namespace Paperless.Api.Repositories;

public class CollectionRepository(PaperlessDbContext db) : ICollectionRepository
{
    public Task<Collection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Collections.Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task AddAsync(Collection collection, CancellationToken cancellationToken = default) => db.Collections.AddAsync(collection, cancellationToken).AsTask();
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
