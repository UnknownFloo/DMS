using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.Entities;

namespace Paperless.Api.Repositories;

//: ======================================================================
//: EF Core access methods for collections and their document links.
//: ======================================================================
public class CollectionRepository(PaperlessDbContext db) : ICollectionRepository
{
    //* Loads one collection with its document links.
    public Task<Collection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Collections.Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    //* Loads all collections with their document links.
    public async Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Collections.Include(x => x.Documents).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    //* Adds a collection.
    public Task AddAsync(Collection collection, CancellationToken cancellationToken = default) => db.Collections.AddAsync(collection, cancellationToken).AsTask();

    //* Deletes a collection.
    public Task DeleteAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        db.Collections.Remove(collection);
        return Task.CompletedTask;
    }

    //* Saves all changes to the database.
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
