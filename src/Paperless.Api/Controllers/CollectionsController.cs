using Microsoft.AspNetCore.Mvc;
using Paperless.Api.DTOs;
using Paperless.Api.Services;

namespace Paperless.Api.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController(CollectionService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CollectionResponse>>> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [HttpPost]
    public async Task<ActionResult<CollectionResponse>> Create(CreateCollectionRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CreateAsync(request, ct)); }
        catch (ArgumentException ex) { return ValidationProblem(ex.Message); }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CollectionResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/documents")]
    public async Task<IActionResult> AddDocument(Guid id, AddDocumentToCollectionRequest request, CancellationToken ct) =>
        await service.AddDocumentAsync(id, request.DocumentId, ct) ? NoContent() : NotFound();

    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> GetDocuments(Guid id, CancellationToken ct) => Ok(await service.GetDocumentsAsync(id, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await service.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> RemoveDocument(Guid id, Guid documentId, CancellationToken ct) =>
        await service.RemoveDocumentAsync(id, documentId, ct) ? NoContent() : NotFound();
}
