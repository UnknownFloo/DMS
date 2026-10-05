using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.DTOs;
using Paperless.Api.Entities;
using Paperless.Api.Mapping;
using Paperless.Api.Repositories;

namespace Paperless.Api.Services;

public class CollectionService(ICollectionRepository collections, PaperlessDbContext db)
{
    public async Task<CollectionResponse> CreateAsync(CreateCollectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name is required.", nameof(request.Name));
        var entity = new Collection
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        await collections.AddAsync(entity, ct);
        await collections.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<IReadOnlyList<CollectionResponse>> GetAllAsync(CancellationToken ct) =>
        (await collections.GetAllAsync(ct)).Select(ToResponse).ToArray();

    public async Task<CollectionResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await collections.GetByIdAsync(id, ct);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await collections.GetByIdAsync(id, ct);
        if (entity is null) return false;

        await collections.DeleteAsync(entity, ct);
        await collections.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AddDocumentAsync(Guid collectionId, Guid documentId, CancellationToken ct)
    {
        var collectionExists = await db.Collections.AnyAsync(x => x.Id == collectionId, ct);
        var documentExists = await db.Documents.AnyAsync(x => x.Id == documentId, ct);
        if (!collectionExists || !documentExists) return false;
        var exists = await db.CollectionDocuments.AnyAsync(x => x.CollectionId == collectionId && x.DocumentId == documentId, ct);
        if (!exists)
        {
            db.CollectionDocuments.Add(new CollectionDocument { CollectionId = collectionId, DocumentId = documentId, AddedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<IReadOnlyList<DocumentResponse>> GetDocumentsAsync(Guid collectionId, CancellationToken ct)
    {
        var documents = await db.CollectionDocuments
            .Where(x => x.CollectionId == collectionId)
            .Include(x => x.Document)
            .ThenInclude(x => x.DocumentTags)
            .ThenInclude(x => x.Tag)
            .OrderByDescending(x => x.AddedAtUtc)
            .Select(x => x.Document)
            .ToListAsync(ct);

        return documents.Select(DocumentMapper.ToResponse).ToArray();
    }

    public async Task<bool> RemoveDocumentAsync(Guid collectionId, Guid documentId, CancellationToken ct)
    {
        var association = await db.CollectionDocuments.FirstOrDefaultAsync(x => x.CollectionId == collectionId && x.DocumentId == documentId, ct);
        if (association is null) return false;

        db.CollectionDocuments.Remove(association);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static CollectionResponse ToResponse(Collection entity) => new(entity.Id, entity.Name, entity.Description, entity.CreatedAtUtc, entity.Documents.Count);
}
