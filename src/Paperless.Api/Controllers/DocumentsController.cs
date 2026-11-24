using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.DTOs;
using Paperless.Api.Services;

namespace Paperless.Api.Controllers;

//: ======================================================================
//: HTTP endpoints for document metadata, file retrieval, search, and uploads.
//: ======================================================================
[ApiController]
[Route("api/documents")]
public class DocumentsController(
    DocumentService service,
    ElasticsearchDocumentSearch documentSearch,
    PaperlessDbContext db) : ControllerBase
{
    //* Lists all stored document metadata records.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    //* Searches indexed documents and returns an empty list for blank queries.
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<DocumentSearchResult>>> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<DocumentSearchResult>());

        var matches = await documentSearch.SearchAsync(q, ct);
        var matchIds = matches.Select(match => match.Id).Distinct().ToArray();
        var existingIds = await db.Documents
            .AsNoTracking()
            .Where(document => matchIds.Contains(document.Id))
            .Select(document => document.Id)
            .ToListAsync(ct);
        var existingIdSet = existingIds.ToHashSet();
        var seen = new HashSet<Guid>();

        return Ok(matches.Where(match => existingIdSet.Contains(match.Id) && seen.Add(match.Id)));
    }

    //* Gets one document or responds with not found.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    //* Streams the original document file inline.
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct)
    {
        var document = await service.GetDocumentForFileAsync(id, ct);
        if (document is null)
        {
            return NotFound();
        }

        var content = await service.OpenDocumentAsync(document.StoragePath, ct);
        if (content is null) return NotFound();
        Response.Headers.ContentDisposition = $"inline; filename=\"{document.FileName}\"";
        return File(content, document.ContentType);
    }

    //* Creates metadata for a document without uploading file content.
    [HttpPost]
    public async Task<ActionResult<DocumentResponse>> Create(CreateDocumentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.CreateAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (ArgumentException ex) { return ValidationProblem(ex.Message); }
    }

    //* Stores an uploaded file and starts the OCR workflow.
    [HttpPost("upload")]
    public async Task<ActionResult<DocumentResponse>> Upload(IFormFile file, CancellationToken ct, [FromForm] string description = "")
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file provided");

            var result = await service.UploadFileAsync(file, description, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    //* Updates document metadata.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentResponse>> Update(Guid id, UpdateDocumentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.UpdateAsync(id, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex) { return ValidationProblem(ex.Message); }
    }

    //* Deletes document metadata and its stored file.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => await service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
