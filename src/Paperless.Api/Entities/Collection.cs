namespace Paperless.Api.Entities;

/// <summary>Additional Sprint-1 use case: user-defined collections for grouping documents.</summary>
public class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<CollectionDocument> Documents { get; set; } = new List<CollectionDocument>();
}
