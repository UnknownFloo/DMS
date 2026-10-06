using Microsoft.AspNetCore.Mvc;
using Paperless.Api.DTOs;
using Paperless.Api.Services;

namespace Paperless.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController(DocumentService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct)
    {
        var document = await service.GetDocumentForFileAsync(id, ct);
        if (document is null)
        {
            return NotFound();
        }

        var filePath = document.StoragePath;
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath, ct);
        Response.Headers.ContentDisposition = $"inline; filename=\"{document.FileName}\"";
        return File(fileBytes, document.ContentType);
    }

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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => await service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
