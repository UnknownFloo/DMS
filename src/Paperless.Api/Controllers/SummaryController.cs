using Microsoft.AspNetCore.Mvc;
using Paperless.Api.DTOs;
using Paperless.Api.Services;
using Paperless.Api.Queues;

namespace Paperless.Api.Controllers;

//: ======================================================================
//: Handles OCR text updates and queues summary generation requests.
//: ======================================================================
[ApiController]
[Route("api/documents")]
public class SummaryController(DocumentService service, IRabbitMqDocumentMessagePublisher publisher) : ControllerBase
{
    //* Updates the stored summary for one document.
    [HttpPut("{id:guid}/summary")]
    public async Task<ActionResult<DocumentResponse>> UpdateSummary(Guid id, SummaryUpdateRequest request, CancellationToken ct)
    {
        var result = await service.UpdateSummaryAsync(id, request.Summary, ct);
        return result is null ? NotFound() : Ok(result);
    }

    //* Updates OCR text for one document.
    [HttpPut("{id:guid}/ocr-text")]
    public async Task<IActionResult> UpdateOcrText(Guid id, OcrTextUpdateRequest request, CancellationToken ct)
    {
        var updated = await service.UpdateOcrTextAsync(id, request.Text, ct);
        return updated ? NoContent() : NotFound();
    }

    //* Queues a GenAI summary regeneration request.
    [HttpPost("{id:guid}/summary/regenerate")]
    public async Task<IActionResult> RegenerateSummary(Guid id, SummaryRegenerateRequest request, CancellationToken ct)
    {
        var document = await service.GetAsync(id, ct);
        var ocr = await service.GetOcrTextAsync(id, ct);
        if (!ocr.Found) return NotFound();
        if (string.IsNullOrWhiteSpace(ocr.OcrText)) return BadRequest("OCR text is not available for this document yet.");

        await service.UpdateSummaryAsync(id, null, ct);
        publisher.PublishGenAiRequest(id, document?.FileName ?? "document.pdf", ocr.OcrText!, request.Prompt);
        return Accepted(new { status = "queued", documentId = id });
    }
}
