namespace Paperless.Api.Entities;

public class CollectionDocument
{
    public Guid CollectionId { get; set; }
    public Collection Collection { get; set; } = null!;
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public DateTime AddedAtUtc { get; set; }
}
