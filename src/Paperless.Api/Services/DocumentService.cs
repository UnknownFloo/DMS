using Paperless.Api.DTOs;
using Paperless.Api.Entities;
using Paperless.Api.Mapping;
using Paperless.Api.Repositories;

namespace Paperless.Api.Services;

public class DocumentService(IDocumentRepository documents, DocumentStorageOptions? storageOptions = null)
{
    private readonly DocumentStorageOptions _storageOptions = storageOptions ?? new DocumentStorageOptions(Path.Combine(AppContext.BaseDirectory, "App_Data", "Uploads"));

    public async Task<DocumentResponse> CreateAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        ValidateName(request.FileName);
        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = request.FileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/pdf" : request.ContentType.Trim(),
            StoragePath = GetStoragePath(request.FileName.Trim()),
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

        if (!string.IsNullOrWhiteSpace(document.StoragePath) && File.Exists(document.StoragePath))
        {
            File.Delete(document.StoragePath);
        }

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
        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = safeFileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            StoragePath = GetStoragePath(safeFileName),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            UploadedAtUtc = DateTime.UtcNow
        };

        var targetPath = document.StoragePath;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await using var stream = File.Create(targetPath);
        await file.CopyToAsync(stream, ct);

        await documents.AddAsync(document, ct);
        await documents.SaveChangesAsync(ct);
        return DocumentMapper.ToResponse(document);
    }

    private string GetStoragePath(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var id = Guid.NewGuid();
        var extension = Path.HasExtension(safeFileName) ? Path.GetExtension(safeFileName) : string.Empty;
        var fileNameWithoutExt = string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(safeFileName))
            ? "document"
            : Path.GetFileNameWithoutExtension(safeFileName);

        return Path.Combine(_storageOptions.RootPath, $"{fileNameWithoutExt}_{id}{extension}");
    }

    private static void ValidateName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("FileName is required.", nameof(fileName));
        if (fileName.Length > 255) throw new ArgumentException("FileName must not exceed 255 characters.", nameof(fileName));
    }
}
