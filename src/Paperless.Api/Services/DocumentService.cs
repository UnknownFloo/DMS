using Paperless.Api.DTOs;
using Paperless.Api.Entities;
using Paperless.Api.Mapping;
using Paperless.Api.Queues;
using Paperless.Api.Repositories;
using Paperless.Api.Storage;

namespace Paperless.Api.Services;

public class DocumentService(
    IDocumentRepository documents,
    DocumentStorageOptions? storageOptions = null,
    IRabbitMqDocumentMessagePublisher? messagePublisher = null,
    IDocumentStorage? documentStorage = null)
{
    private readonly DocumentStorageOptions _storageOptions = storageOptions ?? new DocumentStorageOptions(Path.Combine(AppContext.BaseDirectory, "App_Data", "Uploads"));
    private readonly IRabbitMqDocumentMessagePublisher? _messagePublisher = messagePublisher;
    private readonly IDocumentStorage _documentStorage = documentStorage ?? new LocalDocumentStorage(
        storageOptions?.RootPath ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "Uploads"));

    public async Task<DocumentResponse> CreateAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        ValidateName(request.FileName);
        var documentId = Guid.NewGuid();
        var document = new Document
        {
            Id = documentId,
            FileName = request.FileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/pdf" : request.ContentType.Trim(),
            StoragePath = GetStoragePath(documentId, request.FileName.Trim()),
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

    public async Task<Document?> GetDocumentForFileAsync(Guid id, CancellationToken ct)
    {
        return await documents.GetByIdAsync(id, ct);
    }

    public Task<Stream?> OpenDocumentAsync(string storagePath, CancellationToken ct) => _documentStorage.GetAsync(storagePath, ct);

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

        if (!string.IsNullOrWhiteSpace(document.StoragePath))
            await _documentStorage.DeleteAsync(document.StoragePath, ct);

        await documents.DeleteAsync(document, ct);
        await documents.SaveChangesAsync(ct);
        return true;
    }

    public async Task<DocumentResponse> UploadFileAsync(IFormFile file, string description, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is required.");

        ValidateName(file.FileName);

        var safeFileName = Path.GetFileName(file.FileName).Trim();
        var documentId = Guid.NewGuid();
        var document = new Document
        {
            Id = documentId,
            FileName = safeFileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            StoragePath = GetStoragePath(documentId, safeFileName),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            UploadedAtUtc = DateTime.UtcNow
        };

        await using var stream = file.OpenReadStream();
        await _documentStorage.PutAsync(document.StoragePath, stream, file.Length, document.ContentType, ct);
        try
        {
            await documents.AddAsync(document, ct);
            await documents.SaveChangesAsync(ct);
        }
        catch
        {
            await _documentStorage.DeleteAsync(document.StoragePath, ct);
            throw;
        }

        _messagePublisher?.PublishDocumentUploaded(document.Id, document.FileName, document.Description, document.StoragePath);
        return DocumentMapper.ToResponse(document);
    }

    private static string GetStoragePath(Guid documentId, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        return $"documents/{documentId:N}/{safeFileName}";
    }

    private static void ValidateName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("FileName is required.", nameof(fileName));
        if (fileName.Length > 255) throw new ArgumentException("FileName must not exceed 255 characters.", nameof(fileName));
    }
}
