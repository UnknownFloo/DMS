using Paperless.Api.DTOs;
using Paperless.Api.Entities;
using Paperless.Api.Mapping;
using Paperless.Api.Repositories;

namespace Paperless.Api.Services;

public class DocumentService(IDocumentRepository documents)
{
    public async Task<DocumentResponse> CreateAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        ValidateName(request.FileName);
        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = request.FileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/pdf" : request.ContentType.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            UploadedAtUtc = DateTime.UtcNow
        };

        foreach (var tagName in (request.Tags ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            document.DocumentTags.Add(new DocumentTag { DocumentId = document.Id, Document = document, TagId = Guid.NewGuid(), Tag = new Tag { Name = tagName } });
        }

        await documents.AddAsync(document, ct);
        await documents.SaveChangesAsync(ct);
        return DocumentMapper.ToResponse(document);
    }

    public async Task<DocumentResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var document = await documents.GetByIdAsync(id, ct);
        return document is null ? null : DocumentMapper.ToResponse(document);
    }

    public async Task<IReadOnlyList<DocumentResponse>> GetAllAsync(CancellationToken ct) =>
        (await documents.GetAllAsync(ct)).Select(DocumentMapper.ToResponse).ToArray();

    public async Task<DocumentResponse?> UpdateAsync(Guid id, UpdateDocumentRequest request, CancellationToken ct)
    {
        ValidateName(request.FileName);
        var document = await documents.GetByIdAsync(id, ct);
        if (document is null) return null;
        document.FileName = request.FileName.Trim();
        document.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        document.UpdatedAtUtc = DateTime.UtcNow;
        await documents.SaveChangesAsync(ct);
        return DocumentMapper.ToResponse(document);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var document = await documents.GetByIdAsync(id, ct);
        if (document is null) return false;
        await documents.DeleteAsync(document, ct);
        await documents.SaveChangesAsync(ct);
        return true;
    }

    private static void ValidateName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("FileName is required.", nameof(fileName));
        if (fileName.Length > 255) throw new ArgumentException("FileName must not exceed 255 characters.", nameof(fileName));
    }
}
