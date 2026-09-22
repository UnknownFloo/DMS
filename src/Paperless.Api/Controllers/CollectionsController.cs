using Microsoft.AspNetCore.Mvc;
using Paperless.Api.DTOs;
using Paperless.Api.Services;

namespace Paperless.Api.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController(CollectionService service) : ControllerBase
{
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
}
