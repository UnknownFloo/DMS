namespace Paperless.Api.Entities;

//: ======================================================================
//: Join entity linking collections to their documents.
//: ======================================================================
public class CollectionDocument
{
    public Guid CollectionId { get; set; }
    public Collection Collection { get; set; } = null!;
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public DateTime AddedAtUtc { get; set; }
}
