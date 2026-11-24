namespace Paperless.Api.Entities;

//: ======================================================================
//: Join entity linking documents to tags.
//: ======================================================================

public class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
