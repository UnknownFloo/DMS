using Paperless.Api.DTOs;
using Paperless.Api.Entities;

namespace Paperless.Api.Mapping;

//: ======================================================================
//: Converts persistence entities into the document shape exposed by the API.
//: ======================================================================
public static class DocumentMapper
{
    //* Maps a document entity and sorted tags to a public response.
    public static DocumentResponse ToResponse(Document document) => new(
        document.Id,
        document.FileName,
        document.ContentType,
        document.StoragePath,
        document.Description,
        document.Summary,
        document.OcrText,
        document.UploadedAtUtc,
        document.UpdatedAtUtc,
        document.DocumentTags.Select(x => x.Tag.Name).OrderBy(x => x).ToArray());
}
