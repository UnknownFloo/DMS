namespace Paperless.Api.DTOs;

public record CreateCollectionRequest(string Name, string? Description);
public record CollectionResponse(Guid Id, string Name, string? Description, DateTime CreatedAtUtc, int DocumentCount);
public record AddDocumentToCollectionRequest(Guid DocumentId);
