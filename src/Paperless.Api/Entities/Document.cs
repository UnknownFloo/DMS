namespace Paperless.Api.Entities;

//: ======================================================================
//: Persistence entity for uploaded documents and their OCR or summary results.
//: ======================================================================

// ############################
// Stored document metadata plus extracted text used by search and summaries.
// ############################
public class Document
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public string StoragePath { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Summary { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();
    public ICollection<CollectionDocument> Collections { get; set; } = new List<CollectionDocument>();
    public string? OcrText { get; set; }
}
