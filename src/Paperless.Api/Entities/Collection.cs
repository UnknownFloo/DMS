namespace Paperless.Api.Entities;

//: ======================================================================
//: Persistence entity for named document collections.
//: ======================================================================

// ############################
// A user-defined group for organizing documents.
// ############################
public class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<CollectionDocument> Documents { get; set; } = new List<CollectionDocument>();
}
