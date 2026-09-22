using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.Entities;

namespace Paperless.Api.Repositories;

public class DocumentRepository(PaperlessDbContext db) : IDocumentRepository
{
    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Documents.Include(x => x.DocumentTags).ThenInclude(x => x.Tag).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Documents.Include(x => x.DocumentTags).ThenInclude(x => x.Tag).OrderByDescending(x => x.UploadedAtUtc).ToListAsync(cancellationToken);

    public Task AddAsync(Document document, CancellationToken cancellationToken = default) => db.Documents.AddAsync(document, cancellationToken).AsTask();
    public Task DeleteAsync(Document document, CancellationToken cancellationToken = default)
    {
        db.Documents.Remove(document);
        return Task.CompletedTask;
    }
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
