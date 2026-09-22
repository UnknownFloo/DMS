using Paperless.Api.DTOs;
using Paperless.Api.Entities;

namespace Paperless.Api.Mapping;

public static class DocumentMapper
{
    public static DocumentResponse ToResponse(Document document) => new(
        document.Id,
        document.FileName,
        document.ContentType,
        document.Description,
        document.UploadedAtUtc,
        document.UpdatedAtUtc,
        document.DocumentTags.Select(x => x.Tag.Name).OrderBy(x => x).ToArray());
}
