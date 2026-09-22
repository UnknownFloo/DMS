namespace Paperless.Api.Entities;

public class Document
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public string? Description { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();
    public ICollection<CollectionDocument> Collections { get; set; } = new List<CollectionDocument>();
}
