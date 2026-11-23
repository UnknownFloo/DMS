namespace Paperless.Api.Entities;

//: ======================================================================
//: Persistence entity for document labels.
//: ======================================================================

// ############################
// A reusable label that can be attached to multiple documents.
// ############################
public class Tag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<DocumentTag> Documents { get; set; } = new List<DocumentTag>();
}
