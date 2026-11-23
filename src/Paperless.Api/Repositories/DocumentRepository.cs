using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.Entities;

namespace Paperless.Api.Repositories;

//: ======================================================================
//: EF Core access methods for document records and related data.
//: ======================================================================
public class DocumentRepository(PaperlessDbContext db) : IDocumentRepository
{
    //* Loads one document with its tags.
    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Documents.Include(x => x.DocumentTags).ThenInclude(x => x.Tag).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    //* Loads all documents with their tags, ordered by upload date (newest first).
    public async Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Documents.Include(x => x.DocumentTags).ThenInclude(x => x.Tag).OrderByDescending(x => x.UploadedAtUtc).ToListAsync(cancellationToken);

    //* Adds a new document record.
    public Task AddAsync(Document document, CancellationToken cancellationToken = default) => db.Documents.AddAsync(document, cancellationToken).AsTask();

    //* Deletes an existing document record.
    public Task DeleteAsync(Document document, CancellationToken cancellationToken = default)
    {
        db.Documents.Remove(document);
        return Task.CompletedTask;
    }

    //* Saves all changes made in this context to the database.
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
