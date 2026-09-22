namespace Paperless.Api.DTOs;

public record CreateDocumentRequest(string FileName, string? ContentType, string? Description, IReadOnlyCollection<string>? Tags);
public record UpdateDocumentRequest(string FileName, string? Description);
public record DocumentResponse(Guid Id, string FileName, string ContentType, string? Description, DateTime UploadedAtUtc, DateTime? UpdatedAtUtc, IReadOnlyCollection<string> Tags);
